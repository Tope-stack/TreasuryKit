using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TreasuryKit.BuildingBlocks.Application.Abstractions;
using TreasuryKit.BuildingBlocks.Application.Jobs;
using TreasuryKit.BuildingBlocks.Domain;
using TreasuryKit.Core.Contracts.Queries;
using TreasuryKit.Recon.Application.Configuration;
using TreasuryKit.Recon.Contracts.Matches;
using TreasuryKit.Recon.Domain.Exceptions;
using TreasuryKit.Recon.Domain.Matches;
using TreasuryKit.Recon.Domain.Matching;
using TreasuryKit.Recon.Domain.Rules;
using TreasuryKit.Recon.Domain.Runs;
using CoreDirection = TreasuryKit.Core.Contracts.ExpectedTransactions.CashDirection;

namespace TreasuryKit.Recon.Application.Runs;

/// <summary>
/// Executes a run (plan §5.4): supersede unreviewed proposals and open exceptions → load the unmatched pool from Core →
/// run the engine → store matches, reconciled items and exceptions → publish events. Confirmed matches are never touched,
/// so rerunning is idempotent.
/// </summary>
public sealed class RunReconciliationHandler(
    IReconDbContext db,
    ReconDefaults defaults,
    RunQueue queue,
    ICoreAccountQueries accounts,
    ICoreStatementQueries statements,
    ICoreExpectedTransactionQueries expectedTransactions,
    IEnumerable<IMatchingRule> rules,
    IClock clock,
    ILogger<RunReconciliationHandler> logger) : IJobHandler<RunReconciliationJob>
{
    private readonly Dictionary<string, IMatchingRule> _rules = rules.ToDictionary(r => r.Key, StringComparer.Ordinal);

    public async Task HandleAsync(RunReconciliationJob job, CancellationToken cancellationToken)
    {
        var run = await db.Runs.FirstOrDefaultAsync(r => r.Id == job.RunId, cancellationToken);
        if (run is null || !run.IsActive)
        {
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        run.Start(clock.UtcNow);
        var (ruleConfigs, settings) = await defaults.EnsureAsync(run.TenantId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        var account = await accounts.GetAsync(run.AccountId, cancellationToken);
        if (account is null)
        {
            run.Fail("The bank account no longer exists.", clock.UtcNow);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        var superseded = await SupersedeAsync(run.AccountId, cancellationToken);

        var configured = ruleConfigs
            .Where(c => c.Enabled)
            .OrderBy(c => c.Order)
            .Where(c => _rules.ContainsKey(c.RuleKey)) // SplitPayment/Tolerance arrive in Phase 4
            .Select(c => new ConfiguredRule(_rules[c.RuleKey], new RuleParameters(c.Parameters)))
            .ToList();
        var window = ruleConfigs.Where(c => c.Enabled).Select(c => (int)new RuleParameters(c.Parameters).Get("windowDays", 0m)).DefaultIfEmpty(0).Max();

        var reconciled = (await db.ReconciledItems.AsNoTracking()
                .Where(r => r.AccountId == run.AccountId)
                .Select(r => new { r.ItemType, r.ItemId })
                .ToListAsync(cancellationToken))
            .Select(r => (r.ItemType, r.ItemId))
            .ToHashSet();

        var lines = (await statements.GetLinesAsync(run.AccountId, run.PeriodStart, run.PeriodEnd, cancellationToken))
            .Where(l => !reconciled.Contains((ItemType.StatementLine, l.Id)))
            .Select(l => new PoolLine(
                l.Id, l.ValueDate, l.Amount, l.Currency, l.BankReference, l.EndToEndId, l.NipSessionId, l.InvoiceRefs, l.PaymentRefs,
                l.CounterpartyName, l.NormalizedNarration))
            .ToList();
        var expected = (await expectedTransactions.GetOpenAsync(
                run.AccountId, account.Currency, run.PeriodStart.AddDays(-window), run.PeriodEnd.AddDays(window), cancellationToken))
            .Where(e => !reconciled.Contains((ItemType.ExpectedTransaction, e.Id)))
            .Select(e => new PoolExpected(
                e.Id, e.ExpectedDate, e.OutstandingAmount, e.Currency,
                e.Direction == CoreDirection.Inflow ? CashDirection.Inflow : CashDirection.Outflow,
                e.CanonicalReference, e.CounterpartyNames))
            .ToList();

        // Pairings a reviewer rejected stay rejected (superseded proposals don't count: nobody reviewed them).
        var rejected = (await db.Matches.AsNoTracking()
                .Where(m => m.AccountId == run.AccountId && m.Status == MatchStatus.Rejected && m.Reason != Match.SupersededReason)
                .Include(m => m.Items)
                .ToListAsync(cancellationToken))
            .Select(m => MatchCandidate.ItemSignature(m.Items.Select(i => (i.ItemType, i.ItemId))))
            .ToHashSet(StringComparer.Ordinal);

        var result = MatchingEngine.Run(
            new MatchingPool(lines, expected),
            configured,
            settings.Thresholds,
            e => e.ExpectedDate >= run.PeriodStart && e.ExpectedDate <= run.PeriodEnd,
            rejected);

        var now = clock.UtcNow;
        var currency = Currency.FromCode(account.Currency).Value;
        var lineDates = lines.ToDictionary(l => l.Id, l => l.ValueDate);
        var autoConfirmed = 0;

        foreach (var accepted in result.Matches)
        {
            var auto = accepted.Outcome == MatchOutcome.AutoConfirmed;
            var valueDate = accepted.Candidate.StatementSide.Min(i => lineDates[i.Id]);
            var match = Match.FromEngine(run.TenantId, run.AccountId, run.Id, accepted.Candidate, auto, currency, valueDate, now);
            db.Matches.Add(match);
            foreach (var item in match.Items)
            {
                db.ReconciledItems.Add(new ReconciledItem(run.TenantId, run.AccountId, item.ItemType, item.ItemId, match.Id));
            }

            if (auto)
            {
                autoConfirmed++;
                db.Publish(MatchEvents.Confirmed(match, now));
            }
        }

        foreach (var item in result.Unmatched)
        {
            db.Exceptions.Add(ReconException.Raise(run.TenantId, run.AccountId, run.Id, item, now));
        }

        var stats = new RunStats(
            lines.Count, expected.Count, autoConfirmed, result.Matches.Count - autoConfirmed, result.Unmatched.Count, superseded,
            stopwatch.ElapsedMilliseconds);
        run.Complete(stats, now);
        db.Publish(new ReconciliationRunCompleted(
            Guid.CreateVersion7(), now, run.TenantId, run.AccountId, run.Id,
            new RunStatistics(stats.LinesConsidered, stats.ExpectedConsidered, stats.AutoConfirmed, stats.Proposed, stats.Exceptions, stats.Superseded, stats.DurationMs)));

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation(
            "Run {RunId}: {Auto} auto-confirmed, {Proposed} proposed, {Exceptions} exceptions from {Lines} lines and {Expected} expected",
            run.Id, stats.AutoConfirmed, stats.Proposed, stats.Exceptions, stats.LinesConsidered, stats.ExpectedConsidered);

        if (run.RerunRequested)
        {
            var rerun = await queue.QueueAsync(
                run.TenantId, run.AccountId, run.RerunStart!.Value, run.RerunEnd!.Value, RunTrigger.Event, Match.SystemUser, cancellationToken);
            if (rerun.IsFailure)
            {
                logger.LogWarning("Follow-up run for account {AccountId} not queued: {Error}", run.AccountId, rerun.Error.Message);
            }
        }
    }

    public async Task OnFailedPermanentlyAsync(RunReconciliationJob job, string error, CancellationToken cancellationToken)
    {
        var run = await db.Runs.FirstOrDefaultAsync(r => r.Id == job.RunId, cancellationToken);
        if (run is { IsActive: true })
        {
            run.Fail("The run failed repeatedly; see logs. Last error: " + error.Split('\n')[0], clock.UtcNow);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>Rejects unreviewed proposals and closes open exceptions from earlier runs; their items return to the pool.</summary>
    private async Task<int> SupersedeAsync(Guid accountId, CancellationToken ct)
    {
        var proposals = await db.Matches.Where(m => m.AccountId == accountId && m.Status == MatchStatus.Proposed).ToListAsync(ct);
        var proposalIds = proposals.Select(m => m.Id).ToList();
        proposals.ForEach(m => m.Supersede());
        db.ReconciledItems.RemoveRange(await db.ReconciledItems.Where(r => proposalIds.Contains(r.MatchId)).ToListAsync(ct));

        var exceptions = await db.Exceptions.Where(e => e.AccountId == accountId && e.Status == ExceptionStatus.Open).ToListAsync(ct);
        exceptions.ForEach(e => e.Supersede());

        // Save now so re-proposing the same pairing doesn't collide with the reconciled rows being removed.
        await db.SaveChangesAsync(ct);
        return proposals.Count;
    }
}

internal static class MatchEvents
{
    public static MatchConfirmed Confirmed(Match match, DateTimeOffset now) =>
        new(Guid.CreateVersion7(), now, match.TenantId, match.Id, match.AccountId, Items(match));

    public static IReadOnlyList<MatchedItem> Items(Match match) =>
        match.Items
            .Select(i => new MatchedItem(
                i.ItemType == ItemType.StatementLine ? MatchedItemType.StatementLine : MatchedItemType.ExpectedTransaction,
                i.ItemId,
                i.AllocatedAmount.Amount,
                i.AllocatedAmount.Currency.Code))
            .ToList();
}
