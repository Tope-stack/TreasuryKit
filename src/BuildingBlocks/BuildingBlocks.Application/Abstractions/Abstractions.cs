using TreasuryKit.BuildingBlocks.Application.Events;
using TreasuryKit.BuildingBlocks.Application.Jobs;

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

/// <summary>
/// A module's unit of work. Integration events published and jobs enqueued here are committed atomically
/// with the state changes in the same SaveChanges.
/// </summary>
public interface IModuleUnitOfWork
{
    void Publish(IIntegrationEvent integrationEvent);

    /// <summary>Queues background work in this module's job table; it runs in the Worker after commit.</summary>
    void Enqueue(IJob job);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Starts a transaction spanning several SaveChanges calls and any raw SQL issued through this module.</summary>
    Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
}

public interface IUnitOfWorkTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);
}
