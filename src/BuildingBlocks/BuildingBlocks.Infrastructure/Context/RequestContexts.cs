using TreasuryKit.BuildingBlocks.Application.Abstractions;

namespace TreasuryKit.BuildingBlocks.Infrastructure.Context;

/// <summary>Scoped holder for the current tenant. Set by the HTTP pipeline or by the event bus per message.</summary>
public sealed class TenantContext : ICurrentTenant
{
    public Guid? TenantId { get; private set; }

    public void Set(Guid? tenantId) => TenantId = tenantId;
}

/// <summary>Scoped holder for the current user.</summary>
public sealed class UserContext : ICurrentUser
{
    public const string SystemUserId = "system";

    private static readonly IReadOnlySet<string> NoRoles = new HashSet<string>();

    public string? UserId { get; private set; }

    public IReadOnlySet<string> Roles { get; private set; } = NoRoles;

    public bool IsInRole(string role) => Roles.Contains(role);

    public void Set(string? userId, IEnumerable<string> roles)
    {
        UserId = userId;
        Roles = roles.ToHashSet(StringComparer.Ordinal);
    }

    public void SetSystem() => Set(SystemUserId, []);
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
