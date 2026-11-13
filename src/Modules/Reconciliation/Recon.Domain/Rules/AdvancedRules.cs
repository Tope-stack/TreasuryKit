using TreasuryKit.Recon.Domain.Matching;

namespace TreasuryKit.Recon.Domain.Rules;

/// <summary>
/// One expected transaction paid in several statement lines (ManyToOne), or one line covering several expected
/// transactions (OneToMany), when the parts sum exactly to the target. Bounded subset-sum: parts are pre-filtered by
/// direction, currency and date window, at most <c>maxItems</c> are combined and at most <c>maxCombinations</c> are
/// explored. Only a unique combination is proposed; never auto-confirmed (plan §5.3).
/// </summary>
public sealed class SplitPaymentRule : IMatchingRule
{
    public const decimal BaseScore = 0.85m;
    public const decimal ReferenceBonus = 0.05m;

    public string Key => RuleKeys.SplitPayment;

    public bool CanAutoConfirm => false;

    public IEnumerable<MatchCandidate> Find(MatchingPool pool, RuleParameters parameters)
    {
        var window = (int)parameters.Get("windowDays", 5m);
        var maxItems = (int)parameters.Get("maxItems", 5m);
        var budget = (int)parameters.Get("maxCombinations", 20_000m);
        var candidates = new List<MatchCandidate>();

        // Several lines → one expected transaction.
        foreach (var expected in pool.Expected)
        {
            var parts = pool.Lines
                .Where(l => l.Direction == expected.Direction
                    && l.Currency == expected.Currency
                    && l.Magnitude < expected.Outstanding
                    && Math.Abs(l.ValueDate.DayNumber - expected.ExpectedDate.DayNumber) <= window)
                .ToList();
            var search = SubsetSum.Find(parts, l => l.Magnitude, expected.Outstanding, maxItems, budget);
            if (search.Combinations.Count > 1)
            {
                pool.Flag(ItemType.ExpectedTransaction, expected.Id, ExceptionReason.MultipleCandidates);
                continue;
            }

            if (search.Combinations.Count == 1 && !search.BudgetExhausted)
            {
                var lines = search.Combinations[0];
                var referenced = expected.CanonicalReference is { } reference
                    && lines.All(l => ExactReferenceRule.References(l).Contains(reference));
                candidates.Add(Candidate(
                    MatchType.ManyToOne,
                    lines.Select(l => new ItemRef(ItemType.StatementLine, l.Id, l.Magnitude)).ToList(),
                    [new ItemRef(ItemType.ExpectedTransaction, expected.Id, expected.Outstanding)],
                    referenced));
            }
        }

        // One line → several expected transactions (e.g. a customer settling several invoices at once).
        foreach (var line in pool.Lines)
        {
            var parts = pool.Expected
                .Where(e => e.Direction == line.Direction
                    && e.Currency == line.Currency
                    && e.Outstanding < line.Magnitude
                    && Math.Abs(line.ValueDate.DayNumber - e.ExpectedDate.DayNumber) <= window)
                .ToList();
            var search = SubsetSum.Find(parts, e => e.Outstanding, line.Magnitude, maxItems, budget);
            if (search.Combinations.Count > 1)
            {
                pool.Flag(ItemType.StatementLine, line.Id, ExceptionReason.MultipleCandidates);
                continue;
            }

            if (search.Combinations.Count == 1 && !search.BudgetExhausted)
            {
                var expected = search.Combinations[0];
                var lineRefs = ExactReferenceRule.References(line).ToHashSet(StringComparer.Ordinal);
                var referenced = expected.All(e => e.CanonicalReference is { } r && lineRefs.Contains(r));
                candidates.Add(Candidate(
                    MatchType.OneToMany,
                    [new ItemRef(ItemType.StatementLine, line.Id, line.Magnitude)],
                    expected.Select(e => new ItemRef(ItemType.ExpectedTransaction, e.Id, e.Outstanding)).ToList(),
                    referenced));
            }
        }

        return candidates;
    }

    private MatchCandidate Candidate(MatchType type, IReadOnlyList<ItemRef> statementSide, IReadOnlyList<ItemRef> expectedSide, bool referenced)
    {
        var bonus = referenced ? ReferenceBonus : 0m;
        return new MatchCandidate(
            Key,
            type,
            statementSide,
            expectedSide,
            BaseScore + bonus,
            new Dictionary<string, decimal>
            {
                ["base"] = BaseScore,
                ["reference"] = bonus,
                ["parts"] = Math.Max(statementSide.Count, expectedSide.Count),
            });
    }
}

/// <summary>
/// 1:1 where amounts differ by at most max(absTolerance, pct × expected) — bank charges, FX rounding. The difference is
/// recorded as an adjustment; never auto-confirmed (plan §5.3).
/// </summary>
public sealed class ToleranceRule : IMatchingRule
{
    public const decimal BaseScore = 0.75m;

    public string Key => RuleKeys.Tolerance;

    public bool CanAutoConfirm => false;

    public IEnumerable<MatchCandidate> Find(MatchingPool pool, RuleParameters parameters)
    {
        var absolute = parameters.Get("absTolerance", 0m);
        var pct = parameters.Get("pct", 0.005m);
        var window = (int)parameters.Get("windowDays", 3m);

        bool Fits(PoolLine line, PoolExpected expected)
        {
            if (expected.Direction != line.Direction
                || expected.Currency != line.Currency
                || Math.Abs(line.ValueDate.DayNumber - expected.ExpectedDate.DayNumber) > window)
            {
                return false;
            }

            var difference = Math.Abs(line.Magnitude - expected.Outstanding);
            return difference != 0m && difference <= Math.Max(absolute, pct * expected.Outstanding);
        }

        var candidates = new List<MatchCandidate>();
        foreach (var line in pool.Lines)
        {
            var forLine = pool.Expected.Where(e => Fits(line, e)).ToList();
            if (forLine.Count == 0)
            {
                continue;
            }

            if (forLine.Count > 1 || pool.Lines.Count(l => Fits(l, forLine[0])) > 1)
            {
                pool.Flag(ItemType.StatementLine, line.Id, ExceptionReason.MultipleCandidates);
                forLine.ForEach(e => pool.Flag(ItemType.ExpectedTransaction, e.Id, ExceptionReason.MultipleCandidates));
                continue;
            }

            var expected = forLine[0];
            var referenced = expected.CanonicalReference is { } r && ExactReferenceRule.References(line).Contains(r) ? 0.10m : 0m;
            var named = JaroWinkler.Best(line.CounterpartyName, expected.CounterpartyNames) >= 0.85 ? 0.05m : 0m;
            var adjustment = line.Magnitude - expected.Outstanding;

            candidates.Add(new MatchCandidate(
                Key,
                MatchType.OneToOne,
                [new ItemRef(ItemType.StatementLine, line.Id, line.Magnitude)],
                [new ItemRef(ItemType.ExpectedTransaction, expected.Id, expected.Outstanding)],
                BaseScore + referenced + named,
                new Dictionary<string, decimal>
                {
                    ["base"] = BaseScore,
                    ["reference"] = referenced,
                    ["name"] = named,
                    ["adjustment"] = adjustment,
                },
                adjustment));
        }

        return candidates;
    }
}

/// <summary>Bounded search for item subsets (2..maxItems) whose amounts sum exactly to a target.</summary>
public static class SubsetSum
{
    public sealed record Search<T>(IReadOnlyList<IReadOnlyList<T>> Combinations, bool BudgetExhausted);

    /// <summary>Stops after finding two combinations (enough to know the answer is ambiguous) or exhausting the budget.</summary>
    public static Search<T> Find<T>(IReadOnlyList<T> items, Func<T, decimal> amount, decimal target, int maxItems, int budget)
    {
        var sorted = items.OrderByDescending(amount).ToArray();
        var amounts = sorted.Select(amount).ToArray();

        // suffix[i] = sum of amounts[i..]: prune when even everything left can't reach the target.
        var suffix = new decimal[amounts.Length + 1];
        for (var i = amounts.Length - 1; i >= 0; i--)
        {
            suffix[i] = suffix[i + 1] + amounts[i];
        }

        var found = new List<IReadOnlyList<T>>();
        var chosen = new List<int>();
        var explored = 0;
        var exhausted = false;

        void Walk(int start, decimal remaining)
        {
            for (var i = start; i < amounts.Length && found.Count < 2 && !exhausted; i++)
            {
                if (++explored > budget)
                {
                    exhausted = true;
                    return;
                }

                if (amounts[i] > remaining)
                {
                    continue;
                }

                if (suffix[i] < remaining)
                {
                    return; // the rest is too small
                }

                chosen.Add(i);
                var left = remaining - amounts[i];
                if (left == 0m)
                {
                    if (chosen.Count >= 2)
                    {
                        found.Add(chosen.Select(c => sorted[c]).ToList());
                    }
                }
                else if (chosen.Count < maxItems)
                {
                    Walk(i + 1, left);
                }

                chosen.RemoveAt(chosen.Count - 1);
            }
        }

        if (target > 0m && amounts.Length >= 2)
        {
            Walk(0, target);
        }

        return new Search<T>(found, exhausted);
    }
}
