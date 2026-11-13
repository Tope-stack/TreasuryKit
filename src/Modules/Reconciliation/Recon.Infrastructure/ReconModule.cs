using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TreasuryKit.BuildingBlocks.Application;
using TreasuryKit.BuildingBlocks.Infrastructure.Modules;
using TreasuryKit.BuildingBlocks.Infrastructure.Persistence;
using TreasuryKit.Recon.Infrastructure.Persistence;

namespace TreasuryKit.Recon.Infrastructure;

public sealed class ReconModule : IModule
{
    public string Name => "recon";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<ReconDbContext>(ReconDbContext.SchemaName);
        services.AddHandlersFromAssembly(typeof(Application.AssemblyMarker).Assembly);
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        // Phase 3: runs, matches, exceptions, rules.
    }
}
