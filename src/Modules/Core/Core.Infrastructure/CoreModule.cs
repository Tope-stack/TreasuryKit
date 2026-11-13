using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TreasuryKit.BuildingBlocks.Application;
using TreasuryKit.BuildingBlocks.Infrastructure.Modules;
using TreasuryKit.BuildingBlocks.Infrastructure.Persistence;
using TreasuryKit.Core.Application;
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
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        // Phase 1: accounts, mapping profiles, statement imports.
    }
}
