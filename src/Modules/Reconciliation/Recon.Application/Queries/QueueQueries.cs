using Microsoft.EntityFrameworkCore;
using TreasuryKit.BuildingBlocks.Application.Messaging;
using TreasuryKit.BuildingBlocks.Domain;
using TreasuryKit.Core.Contracts.Queries;
using TreasuryKit.Recon.Domain.Exceptions;
using TreasuryKit.Recon.Domain.Matches;
using TreasuryKit.Recon.Domain.Matching;
using CoreDirection = TreasuryKit.Core.Contracts.ExpectedTransactions.CashDirection;

namespace TreasuryKit.Recon.Application.Queries;

/// <summary>What a reviewer needs to see about one statement line or expected transaction.</summary>
/// <param name="Amount">Signed: inflows positive, outflows negative.</param>
/// <param name="AllocatedAmount">The part of the item covered by the match, when shown in a match.</param>
public sealed record ItemDetail(
    ItemType Type,
    Guid Id,
    DateOnly Date,
    decimal Amount,
    string Currency,
    string Description,
    string? Reference,
    string? Counterparty,
    decimal? AllocatedAmount);

public sealed record MatchDetails(MatchDto Match, IReadOnlyList<ItemDetail> StatementItems, IReadOnlyList<ItemDetail> ExpectedItems);

public sealed record CandidateDetail(decimal Score, IReadOnlyDictionary<string, decimal> ScoreBreakdown, ItemDetail? Item);

public sealed record ExceptionDetails(ExceptionDto Exception, ItemDetail? Item, IReadOnlyList<CandidateDetail> Candidates);

/// <summary>Matches of an account with item details, for the review queue (Proposed) and the matched list (Confirmed).</summary>
public sealed record MatchQueueQuery(Guid AccountId, MatchStatus Status, DateOnly? From, DateOnly? To) : IQuery<IReadOnlyList<MatchDetails>>;

public sealed record ExceptionQueueQuery(Guid AccountId, ExceptionStatus Status) : IQuery<IReadOnlyList<ExceptionDetails>>;

internal sealed class QueueQueryHandlers(
    IReconDbContext db,
    ICoreStatementQueries statements,
    ICoreExpectedTransactionQueries expectedTransactions) :
    IQueryHandler<MatchQueueQuery, IReadOnlyList<MatchDetails>>,
    IQueryHandler<ExceptionQueueQuery, IReadOnlyList<ExceptionDetails>>
{
    private const int MaxResults = 1000;

    public async Task<Result<IReadOnlyList<MatchDetails>>> HandleAsync(MatchQueueQuery q, CancellationToken ct)
    {
        var matches = db.Matches.AsNoTracking().Include(m => m.Items).Where(m => m.AccountId == q.AccountId && m.Status == q.Status);
        if (q.From is { } from)
        {
            matches = matches.Where(m => m.ValueDate >= from);
        }

        if (q.To is { } to)
        {
            matches = matches.Where(m => m.ValueDate <= to);
        }

        var list = await matches.OrderBy(m => m.ValueDate).ThenBy(m => m.Id).Take(MaxResults).ToListAsync(ct);
        var details = await LoadAsync(list.SelectMany(m => m.Items.Select(i => (i.ItemType, i.ItemId))), ct);

        return list.Select(m => new MatchDetails(
                ReconQueryHandlers.ToDto(m),
                Items(m, ItemType.StatementLine),
                Items(m, ItemType.ExpectedTransaction)))
            .ToList();

        IReadOnlyList<ItemDetail> Items(Match m, ItemType type) =>
            m.Items.Where(i => i.ItemType == type)
                .Select(i => details.TryGetValue((type, i.ItemId), out var d) ? d with { AllocatedAmount = i.AllocatedAmount.Amount } : null)
                .OfType<ItemDetail>()
                .ToList();
    }

    public async Task<Result<IReadOnlyList<ExceptionDetails>>> HandleAsync(ExceptionQueueQuery q, CancellationToken ct)
    {
        var exceptions = await db.Exceptions.AsNoTracking()
            .Where(e => e.AccountId == q.AccountId && e.Status == q.Status)
            .OrderBy(e => e.ItemType).ThenBy(e => e.CreatedAt).ThenBy(e => e.Id)
            .Take(MaxResults)
            .ToListAsync(ct);

        var details = await LoadAsync(
            exceptions.SelectMany(e => e.Candidates.Select(c => (c.Type, c.Id)).Append((e.ItemType, e.ItemId))), ct);

        return exceptions.Select(e => new ExceptionDetails(
                new ExceptionDto(e.Id, e.AccountId, e.RunId, e.ItemType, e.ItemId, e.Reason, e.Candidates, e.Status, e.CreatedAt),
                details.GetValueOrDefault((e.ItemType, e.ItemId)),
                e.Candidates.Select(c => new CandidateDetail(c.Score, c.ScoreBreakdown, details.GetValueOrDefault((c.Type, c.Id)))).ToList()))
            .ToList();
    }

    /// <summary>Fetches item details from Core in two round trips.</summary>
    private async Task<Dictionary<(ItemType, Guid), ItemDetail>> LoadAsync(IEnumerable<(ItemType Type, Guid Id)> items, CancellationToken ct)
    {
        var all = items.Distinct().ToList();
        var lineIds = all.Where(i => i.Type == ItemType.StatementLine).Select(i => i.Id).ToList();
        var expectedIds = all.Where(i => i.Type == ItemType.ExpectedTransaction).Select(i => i.Id).ToList();

        var result = new Dictionary<(ItemType, Guid), ItemDetail>();
        if (lineIds.Count > 0)
        {
            foreach (var l in await statements.GetLinesByIdsAsync(lineIds, ct))
            {
                result[(ItemType.StatementLine, l.Id)] = new ItemDetail(
                    ItemType.StatementLine, l.Id, l.ValueDate, l.Amount, l.Currency, l.Narration, l.BankReference, l.CounterpartyName, null);
            }
        }

        if (expectedIds.Count > 0)
        {
            foreach (var e in await expectedTransactions.GetByIdsAsync(expectedIds, ct))
            {
                var signed = e.Direction == CoreDirection.Inflow ? e.Amount : -e.Amount;
                result[(ItemType.ExpectedTransaction, e.Id)] = new ItemDetail(
                    ItemType.ExpectedTransaction, e.Id, e.ExpectedDate, signed, e.Currency, e.Description ?? string.Empty, e.Reference,
                    e.CounterpartyName, null);
            }
        }

        return result;
    }
}
