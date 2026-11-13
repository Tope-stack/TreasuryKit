using System.Diagnostics;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using Serilog;
using TreasuryKit.Api.Auth;
using TreasuryKit.Api.Endpoints;
using TreasuryKit.Api.Middleware;
using TreasuryKit.BuildingBlocks.Infrastructure;
using TreasuryKit.BuildingBlocks.Infrastructure.Authorization;
using TreasuryKit.BuildingBlocks.Infrastructure.Idempotency;
using TreasuryKit.BuildingBlocks.Infrastructure.Modules;
using TreasuryKit.BuildingBlocks.Infrastructure.Persistence;
using TreasuryKit.Core.Infrastructure;
using TreasuryKit.Recon.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Adding a module = one line here (and one in TreasuryKit.Worker).
IModule[] modules = [new CoreModule(), new ReconModule()];

builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

builder.Services.AddBuildingBlocks();
builder.Services.AddPlatformPersistence();
foreach (var module in modules)
{
    module.AddServices(builder.Services, builder.Configuration);
}

builder.Services.AddTreasuryAuthentication();
builder.Services.AddAuthorization(options => options.AddTreasuryPolicies());
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    context.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
    if (context.HttpContext.Items[CorrelationIdMiddleware.ItemKey] is string correlationId)
    {
        context.ProblemDetails.Extensions["correlationId"] = correlationId;
    }
});
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

if (app.Environment.IsDevelopment() || app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value.MigrateOnStartup)
{
    await app.Services.MigrateModuleDatabasesAsync();
}

// Binding failures (e.g. an unknown enum value in the query string) are client errors, not 500s.
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    StatusCodeSelector = exception => exception is BadHttpRequestException badRequest ? badRequest.StatusCode : StatusCodes.Status500InternalServerError,
});
app.UseStatusCodePages();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseAuthentication();
app.UseMiddleware<RequestContextMiddleware>();
app.UseAuthorization();
app.UseMiddleware<IdempotencyMiddleware>();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapMeEndpoint();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference().AllowAnonymous();
    app.MapDevEndpoints();
}

foreach (var module in modules)
{
    module.MapEndpoints(app.MapGroup($"/api/{module.Name}").RequireAuthorization(Policies.CanView));
}

await app.RunAsync();

/// <summary>Entry point; public so integration tests can host it with WebApplicationFactory.</summary>
public partial class Program;
