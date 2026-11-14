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
builder.Services.AddOptions<DemoGateOptions>().BindConfiguration(DemoGateOptions.SectionName);
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

// Single-service hosts (e.g. Render's free tier, ADR 0011) run the Worker's background processing in the Api.
// Hosted services start after the migrations below.
if (builder.Configuration.GetValue<bool>(BackgroundProcessing.RunInApiSetting))
{
    builder.Services.AddBackgroundProcessing();
}
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
if (app.Services.GetRequiredService<IOptions<DemoGateOptions>>().Value.Enabled)
{
    app.UseMiddleware<DemoGateMiddleware>(); // before static files: the whole site is behind the demo password
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseDefaultFiles();
app.UseStaticFiles(); // the web UI, built into wwwroot by `npm run build` in web/
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
}

// The development sign-in (pick any tenant and role) is on in Development, and in a demo deployment
// that opts in explicitly (ADR 0010). It can never be enabled in Production.
if (DevLogin.IsEnabled(app.Environment, app.Configuration))
{
    if (!app.Environment.IsDevelopment())
    {
        app.Logger.LogWarning(
            "Development sign-in is ENABLED in the {Environment} environment: anyone who can reach this site can act as any user. Synthetic data only.",
            app.Environment.EnvironmentName);
    }

    app.MapDevEndpoints();
}

foreach (var module in modules)
{
    module.MapEndpoints(app.MapGroup($"/api/{module.Name}").RequireAuthorization(Policies.CanView));
}

// Client-side routes of the web UI fall back to index.html; API and tooling paths never do.
// `nonfile` keeps asset requests (e.g. /assets/app.js) with the static-file middleware.
app.MapFallbackToFile("{**path:nonfile:regex(^(?!(api|dev|health|openapi|scalar)(/|$)).*$)}", "index.html").AllowAnonymous();

await app.RunAsync();

/// <summary>Entry point; public so integration tests can host it with WebApplicationFactory.</summary>
public partial class Program;
