using TreasuryKit.BuildingBlocks.Application.Events;

namespace TreasuryKit.BuildingBlocks.Application.Abstractions;

public interface ICurrentTenant
{
    /// <summary>The tenant for this request or message; null when there is none (e.g. anonymous dev endpoints).</summary>
    Guid? TenantId { get; }
}

public interface ICurrentUser
{
    /// <summary>The authenticated user's id, or "system" for background work; null when anonymous.</summary>
    string? UserId { get; }

    IReadOnlySet<string> Roles { get; }

    bool IsInRole(string role);
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>A module's unit of work. Integration events published here are committed atomically with state changes.</summary>
public interface IModuleUnitOfWork
{
    void Publish(IIntegrationEvent integrationEvent);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
