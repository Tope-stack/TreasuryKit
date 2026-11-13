using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TreasuryKit.BuildingBlocks.Application;
using TreasuryKit.BuildingBlocks.Infrastructure;
using TreasuryKit.BuildingBlocks.Infrastructure.Modules;
using TreasuryKit.BuildingBlocks.Infrastructure.Persistence;
using TreasuryKit.Core.Application;
using TreasuryKit.Core.Application.Statements;
using TreasuryKit.Core.Application.Settings;
using TreasuryKit.Core.Application.Statements.Parsing;
using TreasuryKit.Core.Domain.Statements.Narration;
using TreasuryKit.Core.Infrastructure.Endpoints;
using TreasuryKit.Core.Infrastructure.Parsing;
using TreasuryKit.Core.Infrastructure.Persistence;

namespace TreasuryKit.Core.Infrastructure;

public sealed class CoreModule : IModule
{
    public string Name => "core";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<CoreDbContext>(CoreDbContext.SchemaName);
        services.AddScoped<ICoreDbContext>(sp => sp.GetRequiredService<CoreDbContext>());
        services.AddHandlersFromAssembly(typeof(ICoreDbContext).Assembly);

        services.AddSingleton<IStatementParser, CsvStatementParser>();
        services.AddSingleton<IStatementParser, XlsxStatementParser>();
        services.AddSingleton<IStatementParser, Mt940StatementParser>();
        services.AddSingleton<IStatementParser, Camt053StatementParser>();
        services.AddSingleton<IStatementParserResolver, StatementParserResolver>();
        services.AddScoped<IStatementLineWriter, StatementLineWriter>();
        services.AddJobHandler<ProcessStatementImportJob, ProcessStatementImportHandler>();
        services.AddJobHandler<ReextractStatementLinesJob, ReextractStatementLinesHandler>();

        // Narration normalization and reference extraction (plan §4.3). Rules are pluggable: register more IReferenceRule.
        services.AddOptions<NarrationOptions>().BindConfiguration(NarrationOptions.SectionName);
        services.AddSingleton<INarrationNormalizer>(sp =>
            new NarrationNormalizer(sp.GetRequiredService<IOptions<NarrationOptions>>().Value.Prefixes));
        services.AddSingleton<IReferenceRule, NipSessionIdRule>();
        services.AddSingleton<IReferenceRule, InvoiceReferenceRule>();
        services.AddSingleton<IReferenceRule, PaymentReferenceRule>();
        services.AddSingleton<IReferenceRule, CounterpartyNameRule>();
        services.AddSingleton<IReferenceExtractor, ReferenceExtractor>();
        services.AddSingleton<LineAnalyzer>();

        services.AddOptions<StatementUploadOptions>().BindConfiguration(StatementUploadOptions.SectionName);
    }

    public void MapEndpoints(IEndpointRouteBuilder app) => CoreEndpoints.Map(app);
}

public sealed class NarrationOptions
{
    public const string SectionName = "Core:Narration";

    /// <summary>Leading channel prefixes stripped from normalized narrations; null uses the built-in list.</summary>
    public List<string>? Prefixes { get; set; }
}
