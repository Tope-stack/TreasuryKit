namespace TreasuryKit.BuildingBlocks.Application.Jobs;

/// <summary>
/// A unit of background work, serialized to the owning module's job table. Handlers must be idempotent:
/// a job can run more than once (retries, lease expiry).
/// </summary>
public interface IJob
{
    /// <summary>The tenant the job runs as.</summary>
    Guid? TenantId { get; }
}

public interface IJobHandler<in TJob>
    where TJob : IJob
{
    Task HandleAsync(TJob job, CancellationToken cancellationToken);

    /// <summary>Called once when the job has exhausted its retries.</summary>
    Task OnFailedPermanentlyAsync(TJob job, string error, CancellationToken cancellationToken) => Task.CompletedTask;
}
