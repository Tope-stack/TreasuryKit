namespace TreasuryKit.Recon.Domain.Matching;

public enum ItemType
{
    StatementLine,
    ExpectedTransaction,
}

public enum CashDirection
{
    Inflow,
    Outflow,
}

public enum MatchType
{
    OneToOne,
    OneToMany,
    ManyToOne,
}

public static class RuleKeys
{
    public const string ExactReference = nameof(ExactReference);
    public const string AmountDateWindow = nameof(AmountDateWindow);
    public const string SplitPayment = nameof(SplitPayment);
    public const string Tolerance = nameof(Tolerance);
    public const string Manual = nameof(Manual);

    public static readonly IReadOnlyList<string> All = [ExactReference, AmountDateWindow, SplitPayment, Tolerance];
}

/// <summary>An unmatched statement line in the pool. <see cref="Amount"/> is signed (credits positive).</summary>
public sealed record PoolLine(
    Guid Id,
    DateOnly ValueDate,
    decimal Amount,
    string Currency,
    string? BankReference,
    string? EndToEndId,
    string? NipSessionId,
    IReadOnlyList<string> InvoiceRefs,
    IReadOnlyList<string> PaymentRefs,
    string? CounterpartyName,
    string NormalizedNarration)
{
    public CashDirection Direction => Amount >= 0 ? CashDirection.Inflow : CashDirection.Outflow;

    public decimal Magnitude => Math.Abs(Amount);
}

/// <summary>An open expected transaction in the pool. <see cref="Outstanding"/> is positive.</summary>
public sealed record PoolExpected(
    Guid Id,
    DateOnly ExpectedDate,
    decimal Outstanding,
    string Currency,
    CashDirection Direction,
    string? CanonicalReference,
    IReadOnlyList<string> CounterpartyNames);

public sealed record ItemRef(ItemType Type, Guid Id, decimal AllocatedAmount);

/// <summary>A proposed pairing from one rule, with an explainable score.</summary>
/// <param name="Adjustment">Statement side minus expected side when a rule accepts a difference (Tolerance); otherwise 0.</param>
public sealed record MatchCandidate(
    string RuleKey,
    MatchType Type,
    IReadOnlyList<ItemRef> StatementSide,
    IReadOnlyList<ItemRef> ExpectedSide,
    decimal Confidence,
    IReadOnlyDictionary<string, decimal> ScoreBreakdown,
    decimal Adjustment = 0m)
{
    /// <summary>Identity of the item set, independent of order; used to remember rejected pairings.</summary>
    public string Signature => ItemSignature(Items);

    public static string ItemSignature(IEnumerable<(ItemType Type, Guid Id)> items) =>
        string.Join('|', items.Select(i => $"{(i.Type == ItemType.StatementLine ? 'L' : 'E')}:{i.Id:N}").Order(StringComparer.Ordinal));

    public IEnumerable<(ItemType Type, Guid Id)> Items =>
        StatementSide.Concat(ExpectedSide).Select(i => (i.Type, i.Id));
}

/// <summary>Numeric rule parameters (plan §5.3), with defaults.</summary>
public sealed class RuleParameters(IReadOnlyDictionary<string, decimal>? values)
{
    public static readonly RuleParameters Empty = new(null);

    public decimal Get(string name, decimal fallback) =>
        values is not null && values.TryGetValue(name, out var value) ? value : fallback;
}
