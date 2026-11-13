using TreasuryKit.BuildingBlocks.Application.Events;
using TreasuryKit.BuildingBlocks.Domain;
using TreasuryKit.BuildingBlocks.Infrastructure.Context;

namespace TreasuryKit.BuildingBlocks.Infrastructure.Persistence;

/// <summary>Dependencies for constructing a module DbContext outside DI (design-time tooling, tests).</summary>
public static class DesignTimeDependencies
{
    public static ModuleDbContextDependencies Create(Guid? tenantId = null)
    {
        var tenant = new TenantContext();
        tenant.Set(tenantId);
        return new ModuleDbContextDependencies(tenant, NoDomainEvents.Instance, new SystemClock());
    }

    private sealed class NoDomainEvents : IDomainEventDispatcher
    {
        public static readonly NoDomainEvents Instance = new();

        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
