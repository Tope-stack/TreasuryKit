namespace TreasuryKit.BuildingBlocks.Domain;

/// <summary>Marks an entity as tenant-scoped. A global query filter restricts it to the current tenant.</summary>
public interface ITenantOwned
{
    Guid TenantId { get; }
}
