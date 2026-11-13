using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using TreasuryKit.BuildingBlocks.Application.Events;
using TreasuryKit.BuildingBlocks.Application.Messaging;

namespace TreasuryKit.BuildingBlocks.Application;

public static class ServiceCollectionExtensions
{
    private static readonly Type[] HandlerInterfaces =
    [
        typeof(ICommandHandler<>),
        typeof(ICommandHandler<,>),
        typeof(IQueryHandler<,>),
        typeof(IDomainEventHandler<>),
    ];

    /// <summary>Registers every command, query and domain event handler in <paramref name="assembly"/> as scoped.</summary>
    public static IServiceCollection AddHandlersFromAssembly(this IServiceCollection services, Assembly assembly)
    {
        var registrations =
            from type in assembly.GetTypes()
            where type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false }
            from iface in type.GetInterfaces()
            where iface.IsGenericType && HandlerInterfaces.Contains(iface.GetGenericTypeDefinition())
            select (iface, type);

        foreach (var (iface, type) in registrations)
        {
            services.AddScoped(iface, type);
        }

        return services;
    }
}
