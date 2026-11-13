using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TreasuryKit.BuildingBlocks.Application;
using TreasuryKit.BuildingBlocks.Infrastructure;
using TreasuryKit.BuildingBlocks.Infrastructure.Modules;
using TreasuryKit.BuildingBlocks.Infrastructure.Persistence;
using TreasuryKit.Core.Contracts.Statements;
using TreasuryKit.Core.Contracts.Tenants;
using TreasuryKit.Recon.Application;
using TreasuryKit.Recon.Application.Configuration;
using TreasuryKit.Recon.Application.Events;
using TreasuryKit.Recon.Application.Reports;
using TreasuryKit.Recon.Infrastructure.Reports;
using TreasuryKit.Recon.Application.Runs;
using TreasuryKit.Recon.Domain.Rules;
using TreasuryKit.Recon.Infrastructure.Endpoints;
using TreasuryKit.Recon.Infrastructure.Persistence;

namespace TreasuryKit.Recon.Infrastructure;

public sealed class ReconModule : IModule
{
    public string Name => "recon";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<ReconDbContext>(ReconDbContext.SchemaName);
        services.AddScoped<IReconDbContext>(sp => sp.GetRequiredService<ReconDbContext>());
        services.AddHandlersFromAssembly(typeof(IReconDbContext).Assembly);

        services.AddScoped<ReconDefaults>();
        services.AddScoped<RunQueue>();

        // Matching rules (plan §5.3), in the order of their keys; per-tenant config decides order and enablement.
        services.AddSingleton<IMatchingRule, ExactReferenceRule>();
        services.AddSingleton<IMatchingRule, AmountDateWindowRule>();
        services.AddSingleton<IMatchingRule, SplitPaymentRule>();
        services.AddSingleton<IMatchingRule, ToleranceRule>();

        services.AddSingleton<IReportExportWriter, ReportExportWriter>();

        services.AddJobHandler<RunReconciliationJob, RunReconciliationHandler>();
        services.AddIntegrationEventHandler<TenantCreated, TenantCreatedHandler, ReconDbContext>();
        services.AddIntegrationEventHandler<StatementImported, StatementImportedHandler, ReconDbContext>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app) => ReconEndpoints.Map(app);
}
