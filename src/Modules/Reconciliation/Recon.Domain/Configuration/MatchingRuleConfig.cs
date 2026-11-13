using TreasuryKit.BuildingBlocks.Domain;
using TreasuryKit.Recon.Domain.Matching;

namespace TreasuryKit.Recon.Domain.Configuration;

/// <summary>A tenant's settings for one matching rule (plan §5.1). Seeded with the defaults from §5.3.</summary>
public sealed class MatchingRuleConfig : AggregateRoot<Guid>, ITenantOwned
{
    /// <summary>Default order and parameters per rule (plan §5.3).</summary>
    public static readonly IReadOnlyList<(string Key, int Order, Dictionary<string, decimal> Parameters)> Defaults =
    [
        (RuleKeys.ExactReference, 1, []),
        (RuleKeys.AmountDateWindow, 2, new() { ["windowDays"] = 3, ["nameSimilarityMin"] = 0.85m }),
        (RuleKeys.SplitPayment, 3, new() { ["windowDays"] = 5, ["maxItems"] = 5, ["maxCombinations"] = 20000 }),
        (RuleKeys.Tolerance, 4, new() { ["absTolerance"] = 0, ["pct"] = 0.005m, ["windowDays"] = 3 }),
    ];

    /// <summary>Accepted parameters and their valid ranges.</summary>
    private static readonly Dictionary<string, Dictionary<string, (decimal Min, decimal Max)>> Ranges = new()
    {
        [RuleKeys.ExactReference] = [],
        [RuleKeys.AmountDateWindow] = new() { ["windowDays"] = (0, 30), ["nameSimilarityMin"] = (0, 1) },
        [RuleKeys.SplitPayment] = new() { ["windowDays"] = (0, 30), ["maxItems"] = (2, 10), ["maxCombinations"] = (1, 1_000_000) },
        [RuleKeys.Tolerance] = new() { ["absTolerance"] = (0, 1_000_000), ["pct"] = (0, 0.1m), ["windowDays"] = (0, 30) },
    };

    private MatchingRuleConfig(Guid id, Guid tenantId)
        : base(id) => TenantId = tenantId;

    private MatchingRuleConfig()
    {
    }

    public Guid TenantId { get; private set; }

    public string RuleKey { get; private set; } = null!;

    public int Order { get; private set; }

    public bool Enabled { get; private set; }

    public Dictionary<string, decimal> Parameters { get; private set; } = [];

    public static IEnumerable<MatchingRuleConfig> CreateDefaults(Guid tenantId) =>
        Defaults.Select(d => new MatchingRuleConfig(Guid.CreateVersion7(), tenantId)
        {
            RuleKey = d.Key,
            Order = d.Order,
            Enabled = true,
            Parameters = new Dictionary<string, decimal>(d.Parameters),
        });

    public Result Update(bool enabled, int order, IReadOnlyDictionary<string, decimal>? parameters)
    {
        if (order is < 1 or > 100)
        {
            return Invalid("order must be between 1 and 100");
        }

        var merged = new Dictionary<string, decimal>(Parameters);
        foreach (var (name, value) in parameters ?? new Dictionary<string, decimal>())
        {
            if (!Ranges[RuleKey].TryGetValue(name, out var range))
            {
                return Invalid($"{RuleKey} has no parameter '{name}' (valid: {string.Join(", ", Ranges[RuleKey].Keys)})");
            }

            if (value < range.Min || value > range.Max)
            {
                return Invalid($"{name} must be between {range.Min} and {range.Max}");
            }

            merged[name] = value;
        }

        Enabled = enabled;
        Order = order;
        Parameters = merged;
        return Result.Success();
    }

    private static Error Invalid(string reason) => Error.Validation("Rule.Invalid", reason + ".");
}

/// <summary>Per-tenant confidence thresholds (plan §5.2).</summary>
public sealed class ReconSettings : ITenantOwned
{
    private ReconSettings()
    {
    }

    public Guid TenantId { get; private set; }

    public decimal AutoConfirmThreshold { get; private set; }

    public decimal ProposeThreshold { get; private set; }

    public static ReconSettings CreateDefault(Guid tenantId) =>
        new() { TenantId = tenantId, AutoConfirmThreshold = Thresholds.Default.AutoConfirm, ProposeThreshold = Thresholds.Default.Propose };

    public Thresholds Thresholds => new(AutoConfirmThreshold, ProposeThreshold);

    public Result Update(decimal autoConfirm, decimal propose)
    {
        if (propose is <= 0m or > 1m || autoConfirm is <= 0m or > 1m || propose > autoConfirm)
        {
            return Error.Validation("Thresholds.Invalid", "Thresholds must satisfy 0 < propose ≤ autoConfirm ≤ 1.");
        }

        AutoConfirmThreshold = autoConfirm;
        ProposeThreshold = propose;
        return Result.Success();
    }
}
