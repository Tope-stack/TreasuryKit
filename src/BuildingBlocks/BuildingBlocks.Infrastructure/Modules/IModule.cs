using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace TreasuryKit.BuildingBlocks.Infrastructure.Modules;

public interface IModule
{
    /// <summary>Lower-case module name; routes are mapped under /api/{Name}.</summary>
    string Name { get; }

    void AddServices(IServiceCollection services, IConfiguration configuration);

    /// <summary>Maps endpoints onto a route group already prefixed with /api/{Name}.</summary>
    void MapEndpoints(IEndpointRouteBuilder app);
}
