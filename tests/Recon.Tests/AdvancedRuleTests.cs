using TreasuryKit.BuildingBlocks.Domain;
using TreasuryKit.Recon.Domain.Matches;
using TreasuryKit.Recon.Domain.Matching;
using TreasuryKit.Recon.Domain.Rules;

namespace TreasuryKit.Recon.Tests;

public sealed class SubsetSumTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Finds_a_unique_combination_of_2_to_5_parts(int parts)
    {
        var amounts = Enumerable.Range(1, parts).Select(i => 1_000m * i + 0.37m * i).ToList();
        var noise = new[] { 7.11m, 13_333.33m, 999.99m };

        var search = SubsetSum.Find(amounts.Concat(noise).ToList(), a => a, amounts.Sum(), maxItems: 5, budget: 20_000);

        var combination = Assert.Single(search.Combinations);
        Assert.Equal(amounts.Order(), combination.Order());
        Assert.False(search.BudgetExhausted);
    }

    [Fact]
    public void Respects_max_items() =>
        Assert.Empty(SubsetSum.Find([1m, 2m, 3m, 4m, 5m, 6m], a => a, 21m, maxItems: 5, budget: 20_000).Combinations);

    [Fact]
    public void Reports_ambiguity_with_two_combinations() =>
        Assert.Equal(2, SubsetSum.Find([10m, 20m, 30m, 5m, 25m], a => a, 30m, maxItems: 5, budget: 20_000).Combinations.Count);

    [Fact]
    public void Single_item_is_not_a_split() =>
        Assert.Empty(SubsetSum.Find([30m, 7m], a => a, 30m, maxItems: 5, budget: 20_000).Combinations);

    [Fact]
    public void Stops_at_the_budget()
    {
        var many = Enumerable.Range(1, 60).Select(i => i * 1.01m).ToList();

        // Reachable in magnitude but never exactly (three decimals), so the search keeps exploring.
        var search = SubsetSum.Find(many, a => a, 250.005m, maxItems: 5, budget: 500);

        Assert.True(search.BudgetExhausted);
    }
}

public sealed class SplitAndToleranceRuleTests
{
    private static readonly DateOnly Day = new(2026, 2, 10);
    private static readonly Thresholds LowThresholds = new(0.10m, 0.05m);

    [Fact]
    public void Split_payment_is_proposed_never_auto_confirmed_even_with_low_thresholds()
    {
        var lines = new[] { Line(100_000m, Day, "INV9001"), Line(150_000m, Day.AddDays(2), "INV9001"), Line(50_000m, Day.AddDays(4), "INV9001") };
        var expected = Expected(300_000m, Day, "INV9001");

        var result = Run(new SplitPaymentRule(), lines, [expected], LowThresholds);

        var match = Assert.Single(result.Matches);
        Assert.Equal(MatchOutcome.Proposed, match.Outcome);
        Assert.Equal(MatchType.ManyToOne, match.Candidate.Type);
        Assert.Equal(3, match.Candidate.StatementSide.Count);
        Assert.Equal(0.90m, match.Candidate.Confidence); // 0.85 + reference bonus
    }

    [Fact]
    public void One_line_settling_several_invoices_is_one_to_many()
    {
        var result = Run(new SplitPaymentRule(), [Line(300m, Day)], [Expected(100m, Day), Expected(200m, Day.AddDays(1))], Thresholds.Default);

        var match = Assert.Single(result.Matches);
        Assert.Equal(MatchType.OneToMany, match.Candidate.Type);
        Assert.Equal(0.85m, match.Candidate.Confidence);
    }

    [Fact]
    public void Split_parts_outside_the_window_are_ignored()
    {
        var result = Run(new SplitPaymentRule(), [Line(100m, Day), Line(200m, Day.AddDays(6))], [Expected(300m, Day)], Thresholds.Default);

        Assert.Empty(result.Matches);
    }

    [Fact]
    public void Ambiguous_split_is_not_proposed()
    {
        var result = Run(new SplitPaymentRule(), [Line(10m, Day), Line(20m, Day), Line(5m, Day), Line(25m, Day)], [Expected(30m, Day)], Thresholds.Default);

        Assert.Empty(result.Matches);
        Assert.Contains(result.Unmatched, u => u.Type == ItemType.ExpectedTransaction && u.Reason == ExceptionReason.MultipleCandidates);
    }

    [Fact]
    public void Tolerance_proposes_with_the_difference_as_adjustment()
    {
        var result = Run(new ToleranceRule(), [Line(215_817.81m, Day, "INV7")], [Expected(215_870.31m, Day, "INV7")], LowThresholds);

        var match = Assert.Single(result.Matches);
        Assert.Equal(MatchOutcome.Proposed, match.Outcome);
        Assert.Equal(-52.50m, match.Candidate.Adjustment);
        Assert.Equal(0.85m, match.Candidate.Confidence); // 0.75 + reference
        Assert.Equal(215_870.31m, match.Candidate.ExpectedSide[0].AllocatedAmount); // settles the expected in full
    }

    [Theory]
    [InlineData(1_000.00, 995.00, true)]   // 0.5% exactly
    [InlineData(1_000.00, 994.99, false)]  // just beyond
    [InlineData(1_000.00, 1_000.00, false)] // equal amounts belong to AmountDateWindow
    public void Tolerance_limit_is_pct_of_the_expected_amount(double expected, double line, bool matches)
    {
        var result = Run(new ToleranceRule(), [Line((decimal)line, Day)], [Expected((decimal)expected, Day)], Thresholds.Default);

        Assert.Equal(matches, result.Matches.Count == 1);
    }

    [Fact]
    public void Absolute_tolerance_applies_when_larger()
    {
        var parameters = new RuleParameters(new Dictionary<string, decimal> { ["absTolerance"] = 50m, ["pct"] = 0.001m });

        var result = MatchingEngine.Run(
            new MatchingPool([Line(960m, Day)], [Expected(1_000m, Day)]),
            [new ConfiguredRule(new ToleranceRule(), parameters)],
            Thresholds.Default,
            _ => true);

        // 40 off: pct allows only 0.001 × 1000 = 1, but the absolute 50 is larger and applies.
        Assert.Equal(-40m, Assert.Single(result.Matches).Candidate.Adjustment);
    }

    [Fact]
    public void Rejected_pairings_are_not_proposed_again()
    {
        var line = Line(4_321m, Day);
        var expected = Expected(4_321m, Day);
        var candidate = new AmountDateWindowRule().Find(new MatchingPool([line], [expected]), RuleParameters.Empty).Single();

        var result = MatchingEngine.Run(
            new MatchingPool([line], [expected]),
            [new ConfiguredRule(new AmountDateWindowRule(), RuleParameters.Empty)],
            Thresholds.Default,
            _ => true,
            new HashSet<string> { candidate.Signature });

        Assert.Empty(result.Matches);
    }

    private static EngineResult Run(IMatchingRule rule, IEnumerable<PoolLine> lines, IEnumerable<PoolExpected> expected, Thresholds thresholds) =>
        MatchingEngine.Run(new MatchingPool(lines, expected), [new ConfiguredRule(rule, RuleParameters.Empty)], thresholds, _ => true);

    private static PoolLine Line(decimal amount, DateOnly date, string? invoice = null) =>
        new(Guid.NewGuid(), date, amount, "NGN", null, null, null, invoice is null ? [] : [invoice], [], null, string.Empty);

    private static PoolExpected Expected(decimal amount, DateOnly date, string? reference = null) =>
        new(Guid.NewGuid(), date, amount, "NGN", CashDirection.Inflow, reference, []);
}

public sealed class MatchLifecycleTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public void Proposer_cannot_confirm_their_own_manual_match()
    {
        var match = Manual("pat");

        Assert.Equal("Match.MakerChecker", match.Confirm("pat", Now).Error.Code);
        Assert.Equal(ErrorType.Forbidden, match.Confirm("pat", Now).Error.Type);
        Assert.True(match.Confirm("amy", Now).IsSuccess);
        Assert.Equal(MatchStatus.Confirmed, match.Status);
        Assert.Equal("amy", match.ConfirmedBy);
    }

    [Fact]
    public void Only_proposed_matches_can_be_confirmed_or_rejected()
    {
        var match = Manual("pat");
        match.Confirm("amy", Now);

        Assert.Equal("Match.NotProposed", match.Confirm("bob", Now).Error.Code);
        Assert.Equal("Match.NotProposed", match.Reject("bob", null, Now).Error.Code);
    }

    [Fact]
    public void Reversal_needs_a_confirmed_match_and_a_reason()
    {
        var match = Manual("pat");
        Assert.Equal("Match.NotConfirmed", match.Reverse("amy", "x", Now).Error.Code);

        match.Confirm("amy", Now);
        Assert.Equal("Match.ReasonRequired", match.Reverse("amy", " ", Now).Error.Code);
        Assert.True(match.Reverse("amy", "Paid to the wrong invoice", Now).IsSuccess);
        Assert.Equal(MatchStatus.Reversed, match.Status);
        Assert.Equal("amy", match.ReviewedBy);
    }

    private static Match Manual(string proposer) =>
        Match.ProposeManual(
            Guid.NewGuid(), Guid.NewGuid(), MatchType.OneToOne,
            [new ItemRef(ItemType.StatementLine, Guid.NewGuid(), 10m)],
            [new ItemRef(ItemType.ExpectedTransaction, Guid.NewGuid(), 10m)],
            Currency.NGN, new DateOnly(2026, 2, 1), 0m, proposer, null, Now);
}
