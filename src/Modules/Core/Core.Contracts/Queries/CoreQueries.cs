using TreasuryKit.Core.Contracts.ExpectedTransactions;

namespace TreasuryKit.Core.Contracts.Queries;

/// <summary>Read-only views of Core data for other modules (plan §4.6). All results are scoped to the current tenant.</summary>
public interface ICoreAccountQueries
{
    Task<AccountSnapshot?> GetAsync(Guid accountId, CancellationToken cancellationToken);
}

public interface ICoreStatementQueries
{
    /// <summary>Statement lines of an account with value date in [from, to].</summary>
    Task<IReadOnlyList<StatementLineSnapshot>> GetLinesAsync(Guid accountId, DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>True when an import overlapping [from, to] is in BalanceMismatch and not yet acknowledged (plan §5.4).</summary>
    Task<bool> HasUnacknowledgedMismatchAsync(Guid accountId, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}

public interface ICoreExpectedTransactionQueries
{
    /// <summary>
    /// Open or partially settled expected transactions dated in [from, to] for this account, plus unassigned ones in the
    /// account's currency.
    /// </summary>
    Task<IReadOnlyList<ExpectedTransactionSnapshot>> GetOpenAsync(
        Guid accountId, string currency, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}

public sealed record AccountSnapshot(Guid Id, string AccountNumber, string Currency, bool IsActive);

/// <param name="Amount">Signed: credits positive.</param>
/// <param name="CounterpartyName">Structured name when the format gave one, else the guess from the narration.</param>
public sealed record StatementLineSnapshot(
    Guid Id,
    Guid AccountId,
    DateOnly BookingDate,
    DateOnly ValueDate,
    decimal Amount,
    string Currency,
    string Narration,
    string NormalizedNarration,
    string? BankReference,
    string? EndToEndId,
    string? NipSessionId,
    IReadOnlyList<string> InvoiceRefs,
    IReadOnlyList<string> PaymentRefs,
    string? CounterpartyName);

/// <param name="OutstandingAmount">Positive; <paramref name="Direction"/> carries the sign.</param>
/// <param name="CounterpartyNames">Normalized counterparty name plus the counterparty's aliases.</param>
public sealed record ExpectedTransactionSnapshot(
    Guid Id,
    Guid? AccountId,
    CashDirection Direction,
    decimal OutstandingAmount,
    string Currency,
    DateOnly ExpectedDate,
    string? Reference,
    string? CanonicalReference,
    IReadOnlyList<string> CounterpartyNames);
