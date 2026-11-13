using System.Diagnostics;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using Serilog;
using TreasuryKit.Api.Auth;
using TreasuryKit.Api.Endpoints;
using TreasuryKit.Api.Middleware;
using TreasuryKit.BuildingBlocks.Infrastructure;
using TreasuryKit.BuildingBlocks.Infrastructure.Authorization;
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

var app = builder.Build();

if (app.Environment.IsDevelopment() || app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value.MigrateOnStartup)
{
    await app.Services.MigrateModuleDatabasesAsync();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseAuthentication();
app.UseMiddleware<RequestContextMiddleware>();
app.UseAuthorization();

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
