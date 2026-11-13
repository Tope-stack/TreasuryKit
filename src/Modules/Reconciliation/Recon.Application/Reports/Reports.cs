using Microsoft.EntityFrameworkCore;
using TreasuryKit.BuildingBlocks.Application.Messaging;
using TreasuryKit.BuildingBlocks.Domain;
using TreasuryKit.Core.Contracts.Queries;
using TreasuryKit.Recon.Domain.Exceptions;
using TreasuryKit.Recon.Domain.Matches;
using TreasuryKit.Recon.Domain.Matching;
using CoreDirection = TreasuryKit.Core.Contracts.ExpectedTransactions.CashDirection;

namespace TreasuryKit.Recon.Application.Reports;

public enum ExportFormat
{
    Csv,
    Xlsx,
}

public sealed record AgingBucket(string Bucket, int Count, decimal Amount);

public sealed record LineSummary(
    int Total,
    int Matched,
    int Proposed,
    int Unmatched,
    decimal MatchedPercentByCount,
    decimal MatchedPercentByAmount,
    decimal TotalAmount,
    decimal MatchedAmount,
    int UnmatchedInflowCount,
    decimal UnmatchedInflowAmount,
    int UnmatchedOutflowCount,
    decimal UnmatchedOutflowAmount);

public sealed record ExpectedSummary(int UnmatchedInflowCount, decimal UnmatchedInflowAmount, int UnmatchedOutflowCount, decimal UnmatchedOutflowAmount);

/// <summary>Reconciliation status of one account and period (plan §5.7). Amounts are absolute values in the account currency.</summary>
public sealed record ReconciliationSummary(
    Guid AccountId,
    DateOnly From,
    DateOnly To,
    string Currency,
    LineSummary Lines,
    ExpectedSummary Expected,
    int PendingProposals,
    IReadOnlyDictionary<string, int> OpenExceptionsByReason,
    IReadOnlyList<AgingBucket> UnmatchedLineAging);

/// <summary>One row of the reconciliation export: a statement line, or an unmatched expected transaction.</summary>
public sealed record ExportRow(
    string ItemType,
    Guid ItemId,
    DateOnly Date,
    decimal Amount,
    string Currency,
    string Description,
    string? Reference,
    string Status,
    Guid? MatchId,
    string? RuleKey,
    decimal? Confidence,
    string? MatchedWith);

public sealed record ExportFile(string FileName, string ContentType, byte[] Content);

public interface IReportExportWriter
{
    ExportFile Write(IReadOnlyList<ExportRow> rows, ExportFormat format, string baseName);
}

public sealed record GetSummaryQuery(Guid AccountId, DateOnly From, DateOnly To) : IQuery<ReconciliationSummary>;

public sealed record ExportQuery(Guid AccountId, DateOnly From, DateOnly To, ExportFormat Format) : IQuery<ExportFile>;

internal sealed class ReportHandlers(
    IReconDbContext db,
    ICoreAccountQueries accounts,
    ICoreStatementQueries statements,
    ICoreExpectedTransactionQueries expectedTransactions,
    IReportExportWriter writer) :
    IQueryHandler<GetSummaryQuery, ReconciliationSummary>,
    IQueryHandler<ExportQuery, ExportFile>
{
    private static readonly (string Name, int MaxDays)[] Buckets = [("0-7", 7), ("8-30", 30), ("31-60", 60), ("60+", int.MaxValue)];

    public async Task<Result<ReconciliationSummary>> HandleAsync(GetSummaryQuery q, CancellationToken ct)
    {
        var data = await LoadAsync(q.AccountId, q.From, q.To, ct);
        if (data.IsFailure)
        {
            return data.Error;
        }

        var d = data.Value;
        var lineStatus = d.Lines.ToDictionary(l => l.Id, l => d.StatusOf(ItemType.StatementLine, l.Id));
        var matched = d.Lines.Where(l => lineStatus[l.Id] == MatchStatus.Confirmed).ToList();
        var unmatched = d.Lines.Where(l => lineStatus[l.Id] is null).ToList();
        var totalAmount = d.Lines.Sum(l => Math.Abs(l.Amount));
        var matchedAmount = matched.Sum(l => Math.Abs(l.Amount));

        var lines = new LineSummary(
            d.Lines.Count,
            matched.Count,
            d.Lines.Count(l => lineStatus[l.Id] == MatchStatus.Proposed),
            unmatched.Count,
            Percent(matched.Count, d.Lines.Count),
            Percent(matchedAmount, totalAmount),
            totalAmount,
            matchedAmount,
            unmatched.Count(l => l.Amount >= 0),
            unmatched.Where(l => l.Amount >= 0).Sum(l => l.Amount),
            unmatched.Count(l => l.Amount < 0),
            unmatched.Where(l => l.Amount < 0).Sum(l => -l.Amount));

        var openExpected = d.UnmatchedExpected;
        var expected = new ExpectedSummary(
            openExpected.Count(e => e.Direction == CoreDirection.Inflow),
            openExpected.Where(e => e.Direction == CoreDirection.Inflow).Sum(e => e.OutstandingAmount),
            openExpected.Count(e => e.Direction == CoreDirection.Outflow),
            openExpected.Where(e => e.Direction == CoreDirection.Outflow).Sum(e => e.OutstandingAmount));

        var exceptions = await db.Exceptions.AsNoTracking()
            .Where(e => e.AccountId == q.AccountId && e.Status == ExceptionStatus.Open)
            .GroupBy(e => e.Reason)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var pending = await db.Matches.AsNoTracking()
            .CountAsync(m => m.AccountId == q.AccountId && m.Status == MatchStatus.Proposed && m.ValueDate >= q.From && m.ValueDate <= q.To, ct);

        var aging = Buckets
            .Select((bucket, i) =>
            {
                var min = i == 0 ? 0 : Buckets[i - 1].MaxDays + 1;
                var inBucket = unmatched.Where(l => (q.To.DayNumber - l.ValueDate.DayNumber) is var age && age >= min && age <= bucket.MaxDays).ToList();
                return new AgingBucket(bucket.Name, inBucket.Count, inBucket.Sum(l => Math.Abs(l.Amount)));
            })
            .ToList();

        return new ReconciliationSummary(
            q.AccountId, q.From, q.To, d.Currency, lines, expected, pending,
            exceptions.ToDictionary(e => e.Key.ToString(), e => e.Count), aging);
    }

    public async Task<Result<ExportFile>> HandleAsync(ExportQuery q, CancellationToken ct)
    {
        var data = await LoadAsync(q.AccountId, q.From, q.To, ct);
        if (data.IsFailure)
        {
            return data.Error;
        }

        var d = data.Value;
        var counterpartIds = d.Matches.Values.SelectMany(m => m.Items).ToList();
        var matchedExpected = (await expectedTransactions.GetByIdsAsync(
                counterpartIds.Where(i => i.ItemType == ItemType.ExpectedTransaction).Select(i => i.ItemId).Distinct().ToList(), ct))
            .ToDictionary(e => e.Id);
        var otherLines = (await statements.GetLinesByIdsAsync(
                counterpartIds.Where(i => i.ItemType == ItemType.StatementLine).Select(i => i.ItemId).Distinct().ToList(), ct))
            .ToDictionary(l => l.Id);

        string Describe(MatchItem item) => item.ItemType == ItemType.ExpectedTransaction
            ? matchedExpected.TryGetValue(item.ItemId, out var e) ? $"{e.Reference ?? e.CounterpartyName ?? e.Id.ToString()} ({e.Amount:N2})" : item.ItemId.ToString()
            : otherLines.TryGetValue(item.ItemId, out var l) ? $"{l.BankReference ?? l.Id.ToString()} ({Math.Abs(l.Amount):N2})" : item.ItemId.ToString();

        var rows = new List<ExportRow>();
        foreach (var line in d.Lines)
        {
            var match = d.MatchFor(ItemType.StatementLine, line.Id);
            rows.Add(new ExportRow(
                "StatementLine", line.Id, line.ValueDate, line.Amount, line.Currency, line.Narration, line.BankReference,
                match is null ? "Unmatched" : match.Status == MatchStatus.Confirmed ? "Matched" : "Proposed",
                match?.Id, match?.RuleKey, match?.Confidence,
                match is null ? null : string.Join("; ", match.Items.Where(i => !(i.ItemType == ItemType.StatementLine && i.ItemId == line.Id)).Select(Describe))));
        }

        foreach (var e in d.UnmatchedExpected)
        {
            rows.Add(new ExportRow(
                "ExpectedTransaction", e.Id, e.ExpectedDate, e.Direction == CoreDirection.Inflow ? e.OutstandingAmount : -e.OutstandingAmount,
                e.Currency, e.Description ?? e.CounterpartyName ?? string.Empty, e.Reference, "Unmatched", null, null, null, null));
        }

        return writer.Write(rows, q.Format, $"reconciliation-{q.AccountId:N}-{q.From:yyyyMMdd}-{q.To:yyyyMMdd}");
    }

    private async Task<Result<ReportData>> LoadAsync(Guid accountId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (to < from || to.DayNumber - from.DayNumber > 366)
        {
            return Error.Validation("Report.InvalidPeriod", "Give a period of at most 366 days with from ≤ to.");
        }

        var account = await accounts.GetAsync(accountId, ct);
        if (account is null)
        {
            return Error.NotFound("Account.NotFound", "Bank account not found.");
        }

        var lines = await statements.GetLinesAsync(accountId, from, to, ct);
        var reconciled = await db.ReconciledItems.AsNoTracking().Where(r => r.AccountId == accountId).ToListAsync(ct);
        var byItem = reconciled.ToDictionary(r => (r.ItemType, r.ItemId), r => r.MatchId);
        var matchIds = reconciled.Select(r => r.MatchId).Distinct().ToList();
        var matches = await db.Matches.AsNoTracking().Include(m => m.Items).Where(m => matchIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, ct);

        var open = await expectedTransactions.GetOpenAsync(accountId, account.Currency, from, to, ct);
        var unmatchedExpected = open.Where(e => !byItem.ContainsKey((ItemType.ExpectedTransaction, e.Id))).ToList();
        return new ReportData(account.Currency, lines, unmatchedExpected, byItem, matches);
    }

    private static decimal Percent(decimal part, decimal total) => total == 0 ? 0 : Math.Round(100m * part / total, 2);

    private sealed record ReportData(
        string Currency,
        IReadOnlyList<StatementLineSnapshot> Lines,
        IReadOnlyList<ExpectedTransactionSnapshot> UnmatchedExpected,
        Dictionary<(ItemType, Guid), Guid> ByItem,
        Dictionary<Guid, Match> Matches)
    {
        public Match? MatchFor(ItemType type, Guid id) =>
            ByItem.TryGetValue((type, id), out var matchId) && Matches.TryGetValue(matchId, out var match) ? match : null;

        public MatchStatus? StatusOf(ItemType type, Guid id) => MatchFor(type, id)?.Status;
    }
}
