using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TreasuryKit.BuildingBlocks.Application;
using TreasuryKit.BuildingBlocks.Infrastructure;
using TreasuryKit.BuildingBlocks.Infrastructure.Modules;
using TreasuryKit.BuildingBlocks.Infrastructure.Persistence;
using TreasuryKit.Core.Application;
using TreasuryKit.Core.Application.Statements;
using TreasuryKit.Core.Application.Statements.Parsing;
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
        services.AddSingleton<IStatementParserResolver, StatementParserResolver>();
        services.AddScoped<IStatementLineWriter, StatementLineWriter>();
        services.AddJobHandler<ProcessStatementImportJob, ProcessStatementImportHandler>();

        services.AddOptions<StatementUploadOptions>().BindConfiguration(StatementUploadOptions.SectionName);
    }

    public void MapEndpoints(IEndpointRouteBuilder app) => CoreEndpoints.Map(app);
}
