using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TreasuryKit.BuildingBlocks.Domain;
using TreasuryKit.Core.Contracts.ExpectedTransactions;
using TreasuryKit.Core.Contracts.Queries;
using TreasuryKit.Core.Domain.ExpectedTransactions;
using TreasuryKit.Core.Domain.Statements;
using TreasuryKit.Core.Domain.Statements.Narration;
using TreasuryKit.Core.Infrastructure.Persistence;

namespace TreasuryKit.Core.Infrastructure.Queries;

/// <summary>Core's query contracts for other modules. Uses Core's DbContext, so the tenant filter applies.</summary>
internal sealed class CoreQueries(CoreDbContext db) : ICoreAccountQueries, ICoreStatementQueries, ICoreExpectedTransactionQueries
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<AccountSnapshot?> GetAsync(Guid accountId, CancellationToken cancellationToken) =>
        await db.BankAccounts.AsNoTracking()
            .Where(a => a.Id == accountId)
            .Select(a => new AccountSnapshot(a.Id, a.AccountNumber, a.Currency.Code, a.IsActive))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<StatementLineSnapshot>> GetLinesAsync(Guid accountId, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        ToSnapshots(await db.StatementLines.AsNoTracking()
            .Where(l => l.AccountId == accountId && l.ValueDate >= from && l.ValueDate <= to)
            .OrderBy(l => l.ValueDate).ThenBy(l => l.Id)
            .ToListAsync(cancellationToken));

    public async Task<IReadOnlyList<StatementLineSnapshot>> GetLinesByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        ToSnapshots(await db.StatementLines.AsNoTracking().Where(l => ids.Contains(l.Id)).ToListAsync(cancellationToken));

    private static IReadOnlyList<StatementLineSnapshot> ToSnapshots(List<StatementLine> lines) =>
        lines.Select(l =>
        {
            var refs = Parse(l.ExtractedRefs);
            return new StatementLineSnapshot(
                l.Id, l.AccountId, l.BookingDate, l.ValueDate, l.Amount.Amount, l.Amount.Currency.Code, l.Narration,
                l.NormalizedNarration, l.BankReference, l.EndToEndId, refs.NipSessionId, refs.InvoiceRefs, refs.PaymentRefs,
                l.CounterpartyName is { } structured ? Domain.Common.TextNormalization.ForName(structured) : l.CounterpartyNameGuess);
        }).ToList();

    public Task<bool> HasUnacknowledgedMismatchAsync(Guid accountId, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        db.StatementImports.AsNoTracking().AnyAsync(
            i => i.AccountId == accountId
                && i.Status == StatementImportStatus.BalanceMismatch
                && (i.PeriodStart == null || i.PeriodStart <= to)
                && (i.PeriodEnd == null || i.PeriodEnd >= from),
            cancellationToken);

    public async Task<IReadOnlyList<ExpectedTransactionSnapshot>> GetOpenAsync(
        Guid accountId, string currency, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var accountCurrency = Currency.FromCode(currency).Value;
        var open = await db.ExpectedTransactions.AsNoTracking()
            .Where(t => (t.Status == ExpectedTransactionStatus.Open || t.Status == ExpectedTransactionStatus.PartiallySettled)
                && t.ExpectedDate >= from && t.ExpectedDate <= to
                && (t.AccountId == accountId || (t.AccountId == null && t.Amount.Currency == accountCurrency)))
            .ToListAsync(cancellationToken);
        return await ToSnapshotsAsync(open, cancellationToken);
    }

    public async Task<IReadOnlyList<ExpectedTransactionSnapshot>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        await ToSnapshotsAsync(await db.ExpectedTransactions.AsNoTracking().Where(t => ids.Contains(t.Id)).ToListAsync(cancellationToken), cancellationToken);

    private async Task<IReadOnlyList<ExpectedTransactionSnapshot>> ToSnapshotsAsync(List<ExpectedTransaction> open, CancellationToken cancellationToken)
    {
        var counterpartyIds = open.Where(t => t.CounterpartyId != null).Select(t => t.CounterpartyId!.Value).Distinct().ToList();
        var counterparties = await db.Counterparties.AsNoTracking()
            .Where(c => counterpartyIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, cancellationToken);

        return open.Select(t =>
        {
            var names = new List<string>();
            if (t.CounterpartyName is { } name)
            {
                names.Add(Domain.Common.TextNormalization.ForName(name));
            }

            if (t.CounterpartyId is { } id && counterparties.TryGetValue(id, out var counterparty))
            {
                names.Add(counterparty.NormalizedName);
                names.AddRange(counterparty.Aliases);
            }

            return new ExpectedTransactionSnapshot(
                t.Id,
                t.AccountId,
                t.Direction == TransactionDirection.Inflow ? CashDirection.Inflow : CashDirection.Outflow,
                t.Amount.Amount,
                t.OutstandingAmount,
                t.Amount.Currency.Code,
                t.ExpectedDate,
                t.Reference,
                t.CanonicalReference,
                names.Where(n => n.Length > 0).Distinct().ToList(),
                t.CounterpartyName,
                t.Description,
                t.Status.ToString());
        }).ToList();
    }

    private static ExtractedReferences Parse(string json)
    {
        try
        {
            var refs = JsonSerializer.Deserialize<ExtractedReferences>(json, Json);
            return refs is null ? ExtractedReferences.None : refs with { InvoiceRefs = refs.InvoiceRefs ?? [], PaymentRefs = refs.PaymentRefs ?? [] };
        }
        catch (JsonException)
        {
            return ExtractedReferences.None;
        }
    }
}
