using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TreasuryKit.BuildingBlocks.Application.Abstractions;
using TreasuryKit.BuildingBlocks.Infrastructure.Persistence;

namespace TreasuryKit.BuildingBlocks.Infrastructure.Jobs;

public interface IJobStore
{
    string Schema { get; }

    /// <summary>Claims and runs one batch of due jobs. Returns the number claimed.</summary>
    Task<int> RunDueJobsAsync(CancellationToken cancellationToken);
}

internal sealed class JobStore<TContext>(
    TContext db,
    JobExecutor executor,
    IClock clock,
    IOptions<JobOptions> options,
    ILogger<JobStore<TContext>> logger) : IJobStore
    where TContext : ModuleDbContext
{
    public string Schema => db.Schema;

    public async Task<int> RunDueJobsAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var now = clock.UtcNow;

        // Claim atomically: take a lease and count the attempt. Committed immediately, so the lease
        // (not a held row lock) keeps other workers away while the job runs.
        // The schema is a compile-time module constant, never user input; values are passed as parameters.
        var sql = $$"""
            UPDATE "{{db.Schema}}".jobs SET locked_until = {0}, attempts = attempts + 1
            WHERE id IN (
                SELECT id FROM "{{db.Schema}}".jobs
                WHERE completed_at IS NULL AND failed_at IS NULL AND run_after <= {1}
                  AND (locked_until IS NULL OR locked_until < {1})
                ORDER BY run_after
                LIMIT {2}
                FOR UPDATE SKIP LOCKED)
            RETURNING *
            """;
        var claimed = await db.Jobs
            .FromSqlRaw(sql, now + settings.Lease, now, settings.BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var job in claimed)
        {
            try
            {
                await executor.ExecuteAsync(job, cancellationToken);
                job.MarkCompleted(clock.UtcNow);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var permanent = job.MarkFailed(ex.ToString(), clock.UtcNow);
                logger.LogWarning(
                    ex,
                    "Job {JobId} ({Type}) in {Schema} failed on attempt {Attempt}{Final}",
                    job.Id,
                    job.Type,
                    db.Schema,
                    job.Attempts,
                    permanent ? " — giving up" : string.Empty);

                if (permanent)
                {
                    await NotifyFailedPermanentlyAsync(job, cancellationToken);
                }
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        return claimed.Count;
    }

    private async Task NotifyFailedPermanentlyAsync(JobRecord job, CancellationToken cancellationToken)
    {
        try
        {
            await executor.NotifyFailedPermanentlyAsync(job, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failure callback for job {JobId} threw", job.Id);
        }
    }
}
