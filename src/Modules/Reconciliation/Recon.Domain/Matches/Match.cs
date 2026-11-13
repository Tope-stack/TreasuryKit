using TreasuryKit.BuildingBlocks.Domain;
using TreasuryKit.Recon.Domain.Matching;

namespace TreasuryKit.Recon.Domain.Matches;

public enum MatchStatus
{
    Proposed,
    Confirmed,
    Rejected,
    Reversed,
}

/// <summary>Statement lines and expected transactions reconciled against each other, with the rule and score that explain it.</summary>
public sealed class Match : AggregateRoot<Guid>, ITenantOwned
{
    public const string SystemUser = "system";
    public const string SupersededReason = "Superseded";

    private readonly List<MatchItem> _items = [];

    private Match(Guid id, Guid tenantId)
        : base(id) => TenantId = tenantId;

    private Match()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid AccountId { get; private set; }

    public Guid? RunId { get; private set; }

    public MatchType Type { get; private set; }

    /// <summary>The rule that produced the match, or <see cref="RuleKeys.Manual"/>.</summary>
    public string RuleKey { get; private set; } = null!;

    public decimal Confidence { get; private set; }

    public Dictionary<string, decimal> ScoreBreakdown { get; private set; } = [];

    public MatchStatus Status { get; private set; }

    public Currency Currency { get; private set; } = null!;

    /// <summary>Earliest value date on the statement side; used to filter matches by period.</summary>
    public DateOnly ValueDate { get; private set; }

    public string ProposedBy { get; private set; } = null!;

    public DateTimeOffset ProposedAt { get; private set; }

    public string? ConfirmedBy { get; private set; }

    public DateTimeOffset? ConfirmedAt { get; private set; }

    public string? Reason { get; private set; }

    public IReadOnlyList<MatchItem> Items => _items;

    /// <summary>A match produced by a run: auto-confirmed above the threshold, otherwise proposed for review.</summary>
    public static Match FromEngine(
        Guid tenantId, Guid accountId, Guid runId, MatchCandidate candidate, bool autoConfirm, Currency currency, DateOnly valueDate, DateTimeOffset now)
    {
        var match = new Match(Guid.CreateVersion7(), tenantId)
        {
            AccountId = accountId,
            RunId = runId,
            Type = candidate.Type,
            RuleKey = candidate.RuleKey,
            Confidence = candidate.Confidence,
            ScoreBreakdown = new Dictionary<string, decimal>(candidate.ScoreBreakdown),
            Status = autoConfirm ? MatchStatus.Confirmed : MatchStatus.Proposed,
            Currency = currency,
            ValueDate = valueDate,
            ProposedBy = SystemUser,
            ProposedAt = now,
            ConfirmedBy = autoConfirm ? SystemUser : null,
            ConfirmedAt = autoConfirm ? now : null,
        };

        foreach (var item in candidate.StatementSide.Concat(candidate.ExpectedSide))
        {
            match._items.Add(new MatchItem(tenantId, match.Id, item.Type, item.Id, new Money(item.AllocatedAmount, currency)));
        }

        return match;
    }

    /// <summary>A newer run replaces this unreviewed proposal (plan §5.4).</summary>
    public void Supersede()
    {
        if (Status != MatchStatus.Proposed)
        {
            throw new InvalidOperationException($"Only proposed matches can be superseded (match {Id} is {Status}).");
        }

        Status = MatchStatus.Rejected;
        Reason = SupersededReason;
    }
}

public sealed class MatchItem : Entity<Guid>, ITenantOwned
{
    public MatchItem(Guid tenantId, Guid matchId, ItemType itemType, Guid itemId, Money allocatedAmount)
        : base(Guid.CreateVersion7())
    {
        TenantId = tenantId;
        MatchId = matchId;
        ItemType = itemType;
        ItemId = itemId;
        AllocatedAmount = allocatedAmount;
    }

    private MatchItem()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid MatchId { get; private set; }

    public ItemType ItemType { get; private set; }

    public Guid ItemId { get; private set; }

    /// <summary>Positive amount of the item covered by this match.</summary>
    public Money AllocatedAmount { get; private set; } = null!;
}

/// <summary>
/// Projection of items in active (proposed or confirmed) matches. Unique per item, so the database itself prevents an item
/// from joining two matches; runs exclude these from the pool (plan §4.6).
/// </summary>
public sealed class ReconciledItem : ITenantOwned
{
    public ReconciledItem(Guid tenantId, Guid accountId, ItemType itemType, Guid itemId, Guid matchId)
    {
        TenantId = tenantId;
        AccountId = accountId;
        ItemType = itemType;
        ItemId = itemId;
        MatchId = matchId;
    }

    private ReconciledItem()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid AccountId { get; private set; }

    public ItemType ItemType { get; private set; }

    public Guid ItemId { get; private set; }

    public Guid MatchId { get; private set; }
}

public static class MatchErrors
{
    public static readonly Error NotFound = Error.NotFound("Match.NotFound", "Match not found.");
}
