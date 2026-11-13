using TreasuryKit.Recon.Domain.Configuration;
using TreasuryKit.Recon.Domain.Matching;
using TreasuryKit.Recon.Domain.Rules;

namespace TreasuryKit.Recon.Tests;

public sealed class MatchingEngineTests
{
    private static readonly DateOnly Day = new(2026, 2, 10);

    private static readonly IReadOnlyList<ConfiguredRule> Rules =
    [
        new(new ExactReferenceRule(), RuleParameters.Empty),
        new(new AmountDateWindowRule(), new RuleParameters(new Dictionary<string, decimal> { ["windowDays"] = 3, ["nameSimilarityMin"] = 0.85m })),
    ];

    [Fact]
    public void Exact_reference_auto_confirms_on_invoice_ref()
    {
        var line = Line(1_000m, invoiceRefs: ["INV5001"]);
        var expected = Expected(1_000m, reference: "INV5001", date: Day.AddDays(10));

        var result = Run([line], [expected]);

        var match = Assert.Single(result.Matches);
        Assert.Equal(MatchOutcome.AutoConfirmed, match.Outcome);
        Assert.Equal(RuleKeys.ExactReference, match.Candidate.RuleKey);
        Assert.Equal(1.0m, match.Candidate.Confidence);
        Assert.Empty(result.Unmatched);
    }

    [Theory]
    [InlineData("nip")]
    [InlineData("bank")]
    [InlineData("payment")]
    [InlineData("e2e")]
    public void Exact_reference_uses_every_line_reference(string kind)
    {
        var line = kind switch
        {
            "nip" => Line(50m, nip: "000013260115143022123456789012"),
            "bank" => Line(50m, bankReference: "FT-2602-000001"),
            "payment" => Line(50m, paymentRefs: ["AB12CD34EF56GH78"]),
            _ => Line(50m, endToEnd: "E2E-777"),
        };
        var reference = kind switch
        {
            "nip" => "000013260115143022123456789012",
            "bank" => "FT2602000001",
            "payment" => "AB12CD34EF56GH78",
            _ => "E2E777",
        };

        Assert.Single(Run([line], [Expected(50m, reference: reference)]).Matches);
    }

    [Fact]
    public void Same_reference_with_different_amount_is_not_matched()
    {
        var result = Run([Line(999.50m, invoiceRefs: ["INV1"])], [Expected(1_000m, reference: "INV1")]);

        Assert.Empty(result.Matches);
        Assert.Equal(2, result.Unmatched.Count);
    }

    [Fact]
    public void Direction_must_agree()
    {
        var result = Run([Line(-1_000m, invoiceRefs: ["INV1"])], [Expected(1_000m, reference: "INV1")]);

        Assert.Empty(result.Matches);
    }

    [Fact]
    public void Two_expected_with_the_same_reference_tie_and_raise_multiple_candidates()
    {
        var line = Line(100m, invoiceRefs: ["INV7"]);

        var result = Run([line], [Expected(100m, reference: "INV7"), Expected(100m, reference: "INV7")]);

        Assert.Empty(result.Matches);
        Assert.All(result.Unmatched, u => Assert.Equal(ExceptionReason.MultipleCandidates, u.Reason));
    }

    [Fact]
    public void Currency_mismatch_is_flagged()
    {
        var line = Line(100m, invoiceRefs: ["INV9"]);
        var expected = Expected(100m, reference: "INV9") with { Currency = "USD" };

        var result = Run([line], [expected]);

        Assert.Empty(result.Matches);
        Assert.All(result.Unmatched, u => Assert.Equal(ExceptionReason.CurrencyMismatch, u.Reason));
    }

    [Theory]
    [InlineData(0, true, "1.0000", MatchOutcome.AutoConfirmed)]
    [InlineData(1, true, "0.9750", MatchOutcome.AutoConfirmed)]
    [InlineData(2, true, "0.9500", MatchOutcome.AutoConfirmed)]
    [InlineData(3, true, "0.9250", MatchOutcome.Proposed)]
    [InlineData(0, false, "0.9000", MatchOutcome.Proposed)]
    public void Amount_date_window_scores_date_and_name(int days, bool sameName, string confidence, MatchOutcome outcome)
    {
        var line = Line(4_321.09m, counterparty: "ADEBAYO TRADING LTD", date: Day.AddDays(days));
        var expected = Expected(4_321.09m, names: [sameName ? "ADEBAYO TRADING LTD" : "SOMEONE ELSE ENTIRELY"]);

        var match = Assert.Single(Run([line], [expected]).Matches);

        Assert.Equal(decimal.Parse(confidence, System.Globalization.CultureInfo.InvariantCulture), match.Candidate.Confidence);
        Assert.Equal(outcome, match.Outcome);
        Assert.Equal(days, match.Candidate.ScoreBreakdown["dateDiffDays"]);
    }

    [Fact]
    public void Similar_names_above_the_minimum_count()
    {
        var line = Line(10m, counterparty: "ADEBAYO TRADING LTD");
        var expected = Expected(10m, names: ["ADEBAYO TRADING LIMITED"]);

        var match = Assert.Single(Run([line], [expected]).Matches);

        Assert.Equal(MatchOutcome.AutoConfirmed, match.Outcome);
        Assert.InRange(match.Candidate.ScoreBreakdown["nameSimilarity"], 0.85m, 0.99m);
    }

    [Fact]
    public void Outside_the_window_there_is_no_candidate()
    {
        var result = Run([Line(10m, date: Day.AddDays(4))], [Expected(10m)]);

        Assert.Empty(result.Matches);
        Assert.Equal(ExceptionReason.NoCandidate, result.Unmatched[0].Reason);
    }

    [Fact]
    public void Two_equal_expected_for_one_line_is_ambiguous_and_never_matched()
    {
        var result = Run([Line(250m, counterparty: "EKO FOODS PLC")], [Expected(250m, names: ["EKO FOODS PLC"]), Expected(250m, names: ["EKO FOODS PLC"])]);

        Assert.Empty(result.Matches);
        Assert.Equal(3, result.Unmatched.Count(u => u.Reason == ExceptionReason.MultipleCandidates));
    }

    [Fact]
    public void Two_equal_lines_for_one_expected_is_ambiguous()
    {
        var result = Run([Line(250m), Line(250m)], [Expected(250m)]);

        Assert.Empty(result.Matches);
    }

    [Fact]
    public void Earlier_rule_removes_items_before_later_rules_run()
    {
        // The line matches E1 by reference; E2 has the same amount and date. Without removal, AmountDateWindow would see ambiguity.
        var line = Line(77m, invoiceRefs: ["INV1"]);
        var byRef = Expected(77m, reference: "INV1");
        var other = Expected(77m);

        var result = Run([line, Line(77m, date: Day.AddDays(1), counterparty: "X")], [byRef, other]);

        Assert.Contains(result.Matches, m => m.Candidate.RuleKey == RuleKeys.ExactReference);
        Assert.Contains(result.Matches, m => m.Candidate.RuleKey == RuleKeys.AmountDateWindow && m.Candidate.ExpectedSide[0].Id == other.Id);
    }

    [Fact]
    public void Below_propose_threshold_becomes_an_exception()
    {
        var result = MatchingEngine.Run(
            new MatchingPool([Line(10m)], [Expected(10m)]), Rules, new Thresholds(0.99m, 0.95m), _ => true);

        Assert.Empty(result.Matches);
        Assert.All(result.Unmatched, u => Assert.Equal(ExceptionReason.BelowThreshold, u.Reason));
    }

    [Fact]
    public void Exceptions_carry_up_to_five_ranked_candidates()
    {
        var line = Line(1_000m);
        var expected = Enumerable.Range(1, 7).Select(i => Expected(1_000m + (i * 10m), date: Day.AddDays(i * 10))).ToList();

        var result = Run([line], expected);

        var exception = result.Unmatched.Single(u => u.Type == ItemType.StatementLine);
        Assert.Equal(MatchingEngine.CandidatesPerException, exception.Candidates.Count);
        Assert.Equal(expected[0].Id, exception.Candidates[0].Id);
        Assert.True(exception.Candidates.Zip(exception.Candidates.Skip(1)).All(p => p.First.Score >= p.Second.Score));
    }

    [Fact]
    public void Expected_outside_the_run_period_raises_no_exception()
    {
        var outside = Expected(5m, date: Day.AddDays(-30));

        var result = MatchingEngine.Run(new MatchingPool([], [outside]), Rules, Thresholds.Default, e => e.ExpectedDate >= Day);

        Assert.Empty(result.Unmatched);
    }

    [Fact]
    public void Default_rule_configs_follow_the_plan()
    {
        var configs = MatchingRuleConfig.CreateDefaults(Guid.NewGuid()).ToList();

        Assert.Equal(RuleKeys.All, configs.OrderBy(c => c.Order).Select(c => c.RuleKey));
        Assert.Equal(3m, configs.Single(c => c.RuleKey == RuleKeys.AmountDateWindow).Parameters["windowDays"]);
        Assert.True(configs.Single(c => c.RuleKey == RuleKeys.Tolerance).Update(true, 4, new Dictionary<string, decimal> { ["pct"] = 0.5m }).IsFailure);
        Assert.True(configs.Single(c => c.RuleKey == RuleKeys.Tolerance).Update(true, 4, new Dictionary<string, decimal> { ["bogus"] = 1m }).IsFailure);
    }

    private static EngineResult Run(IEnumerable<PoolLine> lines, IEnumerable<PoolExpected> expected) =>
        MatchingEngine.Run(new MatchingPool(lines, expected), Rules, Thresholds.Default, _ => true);

    private static PoolLine Line(
        decimal amount,
        DateOnly? date = null,
        string? counterparty = null,
        string? nip = null,
        string? bankReference = null,
        string? endToEnd = null,
        IReadOnlyList<string>? invoiceRefs = null,
        IReadOnlyList<string>? paymentRefs = null) =>
        new(Guid.NewGuid(), date ?? Day, amount, "NGN", bankReference, endToEnd, nip, invoiceRefs ?? [], paymentRefs ?? [], counterparty, string.Empty);

    private static PoolExpected Expected(decimal amount, DateOnly? date = null, string? reference = null, IReadOnlyList<string>? names = null) =>
        new(Guid.NewGuid(), date ?? Day, Math.Abs(amount), "NGN", amount >= 0 ? CashDirection.Inflow : CashDirection.Outflow,
            reference is null ? null : ExactReferenceRule.Canonical(reference), names ?? []);
}

public sealed class JaroWinklerTests
{
    [Theory]
    [InlineData("MARTHA", "MARHTA", 0.9611)]
    [InlineData("DWAYNE", "DUANE", 0.84)]
    [InlineData("DIXON", "DICKSONX", 0.8133)]
    [InlineData("SAME", "SAME", 1.0)]
    [InlineData("ABC", "XYZ", 0.0)]
    public void Matches_reference_values(string a, string b, double expected) =>
        Assert.Equal(expected, JaroWinkler.Similarity(a, b), 3);

    [Fact]
    public void Empty_is_zero() => Assert.Equal(0d, JaroWinkler.Similarity("", "ABC"));
}

public sealed class ReconciliationRunTests
{
    [Fact]
    public void Rerun_requests_widen_to_cover_every_trigger()
    {
        var run = Domain.Runs.ReconciliationRun.Queue(
            Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28), Domain.Runs.RunTrigger.User, "u", DateTimeOffset.UtcNow).Value;

        run.RequestRerun(new DateOnly(2026, 2, 10), new DateOnly(2026, 2, 12));
        run.RequestRerun(new DateOnly(2026, 2, 5), new DateOnly(2026, 2, 11));
        run.RequestRerun(new DateOnly(2026, 2, 11), new DateOnly(2026, 3, 2));

        Assert.True(run.RerunRequested);
        Assert.Equal(new DateOnly(2026, 2, 5), run.RerunStart);
        Assert.Equal(new DateOnly(2026, 3, 2), run.RerunEnd);
    }

    [Theory]
    [InlineData("2026-02-10", "2026-02-09")]
    [InlineData("2025-01-01", "2026-02-01")]
    public void Invalid_periods_are_rejected(string from, string to) =>
        Assert.True(Domain.Runs.ReconciliationRun.Queue(
            Guid.NewGuid(), Guid.NewGuid(), DateOnly.Parse(from, System.Globalization.CultureInfo.InvariantCulture),
            DateOnly.Parse(to, System.Globalization.CultureInfo.InvariantCulture), Domain.Runs.RunTrigger.User, "u", DateTimeOffset.UtcNow).IsFailure);
}
