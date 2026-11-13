using Serilog;
using TreasuryKit.BuildingBlocks.Infrastructure;
using TreasuryKit.BuildingBlocks.Infrastructure.Modules;
using TreasuryKit.BuildingBlocks.Infrastructure.Outbox;
using TreasuryKit.Core.Infrastructure;
using TreasuryKit.Recon.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);

// Adding a module = one line here (and one in TreasuryKit.Api).
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

// The Api owns migrations; the Worker only processes background work.
builder.Services.AddHostedService<OutboxBackgroundService>();

await builder.Build().RunAsync();
