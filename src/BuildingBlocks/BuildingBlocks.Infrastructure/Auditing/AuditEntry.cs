namespace TreasuryKit.BuildingBlocks.Infrastructure.Auditing;

/// <summary>Marks infrastructure entities that are not themselves audited.</summary>
public interface INotAudited;

/// <summary>An append-only record of a change to an entity.</summary>
public sealed class AuditEntry : INotAudited
{
    private AuditEntry()
    {
    }

    public Guid Id { get; private set; }

    public Guid? TenantId { get; private set; }

    public string? UserId { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public string EntityType { get; private set; } = null!;

    public string EntityId { get; private set; } = null!;

    /// <summary>Added, Modified or Deleted.</summary>
    public string Action { get; private set; } = null!;

    /// <summary>JSON of the changed properties before the change; null for inserts.</summary>
    public string? Before { get; private set; }

    /// <summary>JSON of the changed properties after the change; null for deletes.</summary>
    public string? After { get; private set; }

    public static AuditEntry Create(
        Guid? tenantId,
        string? userId,
        DateTimeOffset occurredAt,
        string entityType,
        string entityId,
        string action,
        string? before,
        string? after) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            UserId = userId,
            OccurredAt = occurredAt,
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            Before = before,
            After = after,
        };
}
