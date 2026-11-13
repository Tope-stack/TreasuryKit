using Microsoft.EntityFrameworkCore;
using TreasuryKit.BuildingBlocks.Application.Abstractions;
using TreasuryKit.BuildingBlocks.Application.Jobs;
using TreasuryKit.BuildingBlocks.Domain;
using TreasuryKit.Core.Contracts.Queries;
using TreasuryKit.Recon.Domain.Runs;

namespace TreasuryKit.Recon.Application.Runs;

/// <summary>Executes a queued run in the Worker.</summary>
public sealed record RunReconciliationJob(Guid? TenantId, Guid RunId) : IJob;

/// <summary>Creates runs, enforcing one active run per account (the partial unique index is the final arbiter).</summary>
public sealed class RunQueue(IReconDbContext db, ICoreAccountQueries accounts, ICoreStatementQueries statements, IClock clock)
{
    public async Task<Result<Guid>> QueueAsync(
        Guid tenantId, Guid accountId, DateOnly from, DateOnly to, RunTrigger trigger, string requestedBy, CancellationToken ct)
    {
        var account = await accounts.GetAsync(accountId, ct);
        if (account is null)
        {
            return Error.NotFound("Account.NotFound", "Bank account not found.");
        }

        if (await statements.HasUnacknowledgedMismatchAsync(accountId, from, to, ct))
        {
            return RunErrors.Blocked;
        }

        if (await FindActiveAsync(accountId, ct) is { } active)
        {
            return RunErrors.AlreadyActive(active.Id);
        }

        var run = ReconciliationRun.Queue(tenantId, accountId, from, to, trigger, requestedBy, clock.UtcNow);
        if (run.IsFailure)
        {
            return run.Error;
        }

        db.Runs.Add(run.Value);
        db.Enqueue(new RunReconciliationJob(tenantId, run.Value.Id));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Lost the race for the one-active-run-per-account index.
            db.DiscardPendingChanges();
            if (await FindActiveAsync(accountId, ct) is { } winner)
            {
                return RunErrors.AlreadyActive(winner.Id);
            }

            throw;
        }

        return run.Value.Id;
    }

    public Task<ReconciliationRun?> FindActiveAsync(Guid accountId, CancellationToken ct) =>
        db.Runs.FirstOrDefaultAsync(r => r.AccountId == accountId && (r.Status == RunStatus.Queued || r.Status == RunStatus.Running), ct);
}
