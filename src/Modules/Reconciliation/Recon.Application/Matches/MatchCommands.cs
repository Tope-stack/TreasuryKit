using Microsoft.EntityFrameworkCore;
using TreasuryKit.BuildingBlocks.Application.Abstractions;
using TreasuryKit.BuildingBlocks.Application.Messaging;
using TreasuryKit.BuildingBlocks.Domain;
using TreasuryKit.Core.Contracts.Queries;
using TreasuryKit.Recon.Application.Configuration;
using TreasuryKit.Recon.Application.Runs;
using TreasuryKit.Recon.Contracts.Matches;
using TreasuryKit.Recon.Domain.Exceptions;
using TreasuryKit.Recon.Domain.Matches;
using TreasuryKit.Recon.Domain.Matching;
using TreasuryKit.Recon.Domain.Rules;
using CoreDirection = TreasuryKit.Core.Contracts.ExpectedTransactions.CashDirection;
using MatchType = TreasuryKit.Recon.Domain.Matching.MatchType;

namespace TreasuryKit.Recon.Application.Matches;

/// <summary>A Preparer pairs items by hand (plan §5.5). Needs a different Approver to confirm.</summary>
public sealed record ProposeManualMatchCommand(
    IReadOnlyList<Guid> StatementLineIds,
    IReadOnlyList<Guid> ExpectedTransactionIds,
    string? Reason) : ICommand<Guid>;

public sealed record ConfirmMatchCommand(Guid MatchId) : ICommand;

public sealed record RejectMatchCommand(Guid MatchId, string? Reason) : ICommand;

public sealed record ReverseMatchCommand(Guid MatchId, string? Reason) : ICommand;

public sealed record ConfirmMatchesCommand(IReadOnlyList<Guid> MatchIds) : ICommand<BatchConfirmResult>;

public sealed record BatchConfirmFailure(Guid MatchId, string Code, string Message);

public sealed record BatchConfirmResult(IReadOnlyList<Guid> Confirmed, IReadOnlyList<BatchConfirmFailure> Failed);

public sealed record ResolveExceptionCommand(Guid ExceptionId, string? Note) : ICommand;

public sealed record IgnoreExceptionCommand(Guid ExceptionId, string? Note) : ICommand;

internal sealed class ProposeManualMatchHandler(
    IReconDbContext db,
    ReconDefaults defaults,
    ICoreStatementQueries statements,
    ICoreExpectedTransactionQueries expectedTransactions,
    ICoreAccountQueries accounts,
    ICurrentTenant tenant,
    ICurrentUser user,
    IClock clock) : ICommandHandler<ProposeManualMatchCommand, Guid>
{
    private const int MaxItemsPerSide = 50;

    public async Task<Result<Guid>> HandleAsync(ProposeManualMatchCommand c, CancellationToken ct)
    {
        var lineIds = (c.StatementLineIds ?? []).Distinct().ToList();
        var expectedIds = (c.ExpectedTransactionIds ?? []).Distinct().ToList();
        if (lineIds.Count == 0 || expectedIds.Count == 0)
        {
            return Invalid("Give at least one statement line and one expected transaction.");
        }

        if (lineIds.Count > MaxItemsPerSide || expectedIds.Count > MaxItemsPerSide)
        {
            return Invalid($"At most {MaxItemsPerSide} items per side.");
        }

        if (lineIds.Count > 1 && expectedIds.Count > 1)
        {
            return Invalid("Many-to-many matches are not supported; split them into one-to-many or many-to-one matches.");
        }

        var lines = await statements.GetLinesByIdsAsync(lineIds, ct);
        var expected = await expectedTransactions.GetByIdsAsync(expectedIds, ct);
        if (lines.Count != lineIds.Count || expected.Count != expectedIds.Count)
        {
            return Error.NotFound("Match.ItemNotFound", "One or more statement lines or expected transactions were not found.");
        }

        var accountId = lines[0].AccountId;
        var account = await accounts.GetAsync(accountId, ct);
        if (account is null || lines.Any(l => l.AccountId != accountId))
        {
            return Invalid("All statement lines must belong to the same account.");
        }

        if (expected.Any(e => e.AccountId is { } a && a != accountId))
        {
            return Invalid("An expected transaction is assigned to a different account.");
        }

        if (lines.Any(l => l.Currency != account.Currency) || expected.Any(e => e.Currency != account.Currency))
        {
            return Invalid($"All items must be in the account's currency ({account.Currency}).");
        }

        if (expected.Any(e => e.Status is not ("Open" or "PartiallySettled")))
        {
            return Error.Conflict("Match.ExpectedNotOpen", "Only open or partially settled expected transactions can be matched.");
        }

        var lineDirection = lines[0].Amount >= 0 ? CoreDirection.Inflow : CoreDirection.Outflow;
        if (lines.Any(l => (l.Amount >= 0 ? CoreDirection.Inflow : CoreDirection.Outflow) != lineDirection)
            || expected.Any(e => e.Direction != lineDirection))
        {
            return Invalid("All items must have the same direction (inflow or outflow).");
        }

        var taken = await db.ReconciledItems.AsNoTracking()
            .AnyAsync(r => (r.ItemType == ItemType.StatementLine && lineIds.Contains(r.ItemId))
                || (r.ItemType == ItemType.ExpectedTransaction && expectedIds.Contains(r.ItemId)), ct);
        if (taken)
        {
            return MatchErrors.ItemsAlreadyMatched;
        }

        // Both sides must balance, or differ within the tenant's Tolerance settings (recorded as an adjustment).
        var statementTotal = lines.Sum(l => Math.Abs(l.Amount));
        var expectedTotal = expected.Sum(e => e.OutstandingAmount);
        var adjustment = statementTotal - expectedTotal;
        if (adjustment != 0m)
        {
            var (rules, _) = await defaults.EnsureAsync(tenant.TenantId!.Value, ct);
            var tolerance = new RuleParameters(rules.FirstOrDefault(r => r.RuleKey == RuleKeys.Tolerance)?.Parameters);
            var allowed = Math.Max(tolerance.Get("absTolerance", 0m), tolerance.Get("pct", 0.005m) * expectedTotal);
            if (Math.Abs(adjustment) > allowed)
            {
                return new Error(
                    "Match.Unbalanced",
                    $"Statement side {statementTotal:N2} and expected side {expectedTotal:N2} differ by {adjustment:N2}, more than the allowed {allowed:N2}.",
                    ErrorType.Failure);
            }
        }

        var type = lines.Count > 1 ? MatchType.ManyToOne : expected.Count > 1 ? MatchType.OneToMany : MatchType.OneToOne;
        var userId = user.UserId!;
        var match = Match.ProposeManual(
            tenant.TenantId!.Value,
            accountId,
            type,
            lines.Select(l => new ItemRef(ItemType.StatementLine, l.Id, Math.Abs(l.Amount))).ToList(),
            expected.Select(e => new ItemRef(ItemType.ExpectedTransaction, e.Id, e.OutstandingAmount)).ToList(),
            Currency.FromCode(account.Currency).Value,
            lines.Min(l => l.ValueDate),
            adjustment,
            userId,
            c.Reason,
            clock.UtcNow);

        db.Matches.Add(match);
        foreach (var item in match.Items)
        {
            db.ReconciledItems.Add(new ReconciledItem(match.TenantId, accountId, item.ItemType, item.ItemId, match.Id));
        }

        var itemIds = lineIds.Concat(expectedIds).ToList();
        foreach (var exception in await db.Exceptions.Where(e => e.Status == ExceptionStatus.Open && itemIds.Contains(e.ItemId)).ToListAsync(ct))
        {
            exception.ResolveByMatch(match.Id, userId);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.DiscardPendingChanges();
            return MatchErrors.ItemsAlreadyMatched; // lost a race on reconciled_items
        }

        return match.Id;
    }

    private static Error Invalid(string message) => Error.Validation("Match.Invalid", message);
}

/// <summary>Confirm, reject and reverse share loading, concurrency handling and event publishing.</summary>
internal sealed class MatchReviewHandlers(IReconDbContext db, ICurrentUser user, IClock clock) :
    ICommandHandler<ConfirmMatchCommand>,
    ICommandHandler<RejectMatchCommand>,
    ICommandHandler<ReverseMatchCommand>,
    ICommandHandler<ConfirmMatchesCommand, BatchConfirmResult>
{
    public Task<Result> HandleAsync(ConfirmMatchCommand command, CancellationToken ct) => ConfirmAsync(command.MatchId, ct);

    public Task<Result> HandleAsync(RejectMatchCommand command, CancellationToken ct) =>
        ReviewAsync(command.MatchId, ct, async match =>
        {
            var result = match.Reject(UserId, command.Reason, clock.UtcNow);
            if (result.IsSuccess)
            {
                await ReleaseItemsAsync(match, ct);
            }

            return result;
        });

    public Task<Result> HandleAsync(ReverseMatchCommand command, CancellationToken ct) =>
        ReviewAsync(command.MatchId, ct, async match =>
        {
            var now = clock.UtcNow;
            var result = match.Reverse(UserId, command.Reason, now);
            if (result.IsSuccess)
            {
                await ReleaseItemsAsync(match, ct);
                db.Publish(new MatchReversed(Guid.CreateVersion7(), now, match.TenantId, match.Id, match.AccountId, MatchEvents.Items(match)));
            }

            return result;
        });

    public async Task<Result<BatchConfirmResult>> HandleAsync(ConfirmMatchesCommand command, CancellationToken ct)
    {
        var ids = (command.MatchIds ?? []).Distinct().ToList();
        if (ids.Count is 0 or > 500)
        {
            return Error.Validation("Match.Invalid", "Give between 1 and 500 match ids.");
        }

        var confirmed = new List<Guid>();
        var failed = new List<BatchConfirmFailure>();
        foreach (var id in ids)
        {
            var result = await ConfirmAsync(id, ct);
            if (result.IsSuccess)
            {
                confirmed.Add(id);
            }
            else
            {
                failed.Add(new BatchConfirmFailure(id, result.Error.Code, result.Error.Message));
            }

            db.DiscardPendingChanges(); // each match is its own unit of work
        }

        return new BatchConfirmResult(confirmed, failed);
    }

    private Task<Result> ConfirmAsync(Guid matchId, CancellationToken ct) =>
        ReviewAsync(matchId, ct, match =>
        {
            var now = clock.UtcNow;
            var result = match.Confirm(UserId, now);
            if (result.IsSuccess)
            {
                db.Publish(MatchEvents.Confirmed(match, now));
            }

            return Task.FromResult(result);
        });

    private async Task<Result> ReviewAsync(Guid matchId, CancellationToken ct, Func<Match, Task<Result>> action)
    {
        var match = await db.Matches.Include(m => m.Items).FirstOrDefaultAsync(m => m.Id == matchId, ct);
        if (match is null)
        {
            return MatchErrors.NotFound;
        }

        var result = await action(match);
        if (result.IsFailure)
        {
            return result;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.DiscardPendingChanges();
            return MatchErrors.ConcurrentChange;
        }

        return Result.Success();
    }

    /// <summary>Items of a rejected or reversed match go back to the pool.</summary>
    private async Task ReleaseItemsAsync(Match match, CancellationToken ct) =>
        db.ReconciledItems.RemoveRange(await db.ReconciledItems.Where(r => r.MatchId == match.Id).ToListAsync(ct));

    private string UserId => user.UserId ?? throw new InvalidOperationException("No current user.");
}

internal sealed class ExceptionHandlers(IReconDbContext db, ICurrentUser user) :
    ICommandHandler<ResolveExceptionCommand>,
    ICommandHandler<IgnoreExceptionCommand>
{
    public Task<Result> HandleAsync(ResolveExceptionCommand command, CancellationToken ct) =>
        CloseAsync(command.ExceptionId, e => e.Resolve(user.UserId!, command.Note), ct);

    public Task<Result> HandleAsync(IgnoreExceptionCommand command, CancellationToken ct) =>
        CloseAsync(command.ExceptionId, e => e.Ignore(user.UserId!, command.Note), ct);

    private async Task<Result> CloseAsync(Guid id, Func<ReconException, Result> close, CancellationToken ct)
    {
        var exception = await db.Exceptions.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (exception is null)
        {
            return ReconException.NotFound;
        }

        var result = close(exception);
        if (result.IsSuccess)
        {
            await db.SaveChangesAsync(ct);
        }

        return result;
    }
}
