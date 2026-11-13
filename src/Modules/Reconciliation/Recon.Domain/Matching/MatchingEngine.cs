using TreasuryKit.Recon.Domain.Rules;

namespace TreasuryKit.Recon.Domain.Matching;

public sealed record Thresholds(decimal AutoConfirm, decimal Propose)
{
    public static readonly Thresholds Default = new(0.95m, 0.70m);
}

public sealed record ConfiguredRule(IMatchingRule Rule, RuleParameters Parameters);

public enum MatchOutcome
{
    AutoConfirmed,
    Proposed,
}

public sealed record AcceptedMatch(MatchCandidate Candidate, MatchOutcome Outcome);

/// <summary>One of the closest items on the other side, shown with an exception to help a human decide.</summary>
public sealed record ExceptionCandidate(ItemType Type, Guid Id, decimal Score, IReadOnlyDictionary<string, decimal> ScoreBreakdown);

public sealed record UnmatchedItem(ItemType Type, Guid Id, ExceptionReason Reason, IReadOnlyList<ExceptionCandidate> Candidates);

public sealed record EngineResult(IReadOnlyList<AcceptedMatch> Matches, IReadOnlyList<UnmatchedItem> Unmatched);

/// <summary>
/// The matching pipeline (plan §5.2): for each enabled rule in order → candidates → resolve conflicts (an item joins at
/// most one match; highest confidence wins; a tie means no winner and a MultipleCandidates exception) → thresholds →
/// remove matched items → next rule. Leftovers become exceptions with their top-5 candidates.
/// </summary>
public static class MatchingEngine
{
    public const int CandidatesPerException = 5;

    /// <param name="exceptionsFor">Expected transactions outside the run period stay in the pool as candidates but don't raise exceptions.</param>
    public static EngineResult Run(
        MatchingPool pool,
        IReadOnlyList<ConfiguredRule> rules,
        Thresholds thresholds,
        Func<PoolExpected, bool> exceptionsFor)
    {
        var accepted = new List<AcceptedMatch>();

        foreach (var (rule, parameters) in rules)
        {
            var candidates = rule.Find(pool, parameters).ToList();
            foreach (var candidate in Resolve(pool, candidates))
            {
                if (candidate.Confidence >= thresholds.AutoConfirm)
                {
                    accepted.Add(new AcceptedMatch(candidate, MatchOutcome.AutoConfirmed));
                }
                else if (candidate.Confidence >= thresholds.Propose)
                {
                    accepted.Add(new AcceptedMatch(candidate, MatchOutcome.Proposed));
                }
                else
                {
                    foreach (var (type, id) in candidate.Items)
                    {
                        pool.Flag(type, id, ExceptionReason.BelowThreshold);
                    }

                    continue;
                }

                foreach (var (type, id) in candidate.Items)
                {
                    pool.Remove(type, id);
                }
            }
        }

        var unmatched = pool.Lines
            .Select(l => new UnmatchedItem(ItemType.StatementLine, l.Id, pool.ReasonFor(ItemType.StatementLine, l.Id), Rank(pool, l)))
            .Concat(pool.Expected.Where(exceptionsFor).Select(e =>
                new UnmatchedItem(ItemType.ExpectedTransaction, e.Id, pool.ReasonFor(ItemType.ExpectedTransaction, e.Id), Rank(pool, e))))
            .ToList();

        return new EngineResult(accepted, unmatched);
    }

    /// <summary>Highest confidence first; a candidate wins only if none of its items is taken or tied at the same confidence.</summary>
    private static List<MatchCandidate> Resolve(MatchingPool pool, List<MatchCandidate> candidates)
    {
        var winners = new List<MatchCandidate>();
        var taken = new HashSet<(ItemType, Guid)>();

        foreach (var group in candidates.Where(c => c.Items.All(i => pool.Contains(i.Type, i.Id)))
                     .GroupBy(c => c.Confidence)
                     .OrderByDescending(g => g.Key))
        {
            var level = group.Where(c => !c.Items.Any(taken.Contains)).ToList();
            var usage = level.SelectMany(c => c.Items).GroupBy(i => i).ToDictionary(g => g.Key, g => g.Count());

            foreach (var candidate in level)
            {
                if (candidate.Items.Any(i => usage[i] > 1))
                {
                    // Tied with another candidate over the same item: neither wins.
                    foreach (var (type, id) in candidate.Items)
                    {
                        pool.Flag(type, id, ExceptionReason.MultipleCandidates);
                    }

                    continue;
                }

                winners.Add(candidate);
            }

            // Items in a tie are not available to lower-confidence candidates either.
            foreach (var candidate in level)
            {
                taken.UnionWith(candidate.Items);
            }
        }

        return winners;
    }

    private static IReadOnlyList<ExceptionCandidate> Rank(MatchingPool pool, PoolLine line) =>
        pool.Expected
            .Where(e => e.Direction == line.Direction && e.Currency == line.Currency)
            .Select(e => Score(line, e, ItemType.ExpectedTransaction, e.Id))
            .OrderByDescending(c => c.Score)
            .Take(CandidatesPerException)
            .ToList();

    private static IReadOnlyList<ExceptionCandidate> Rank(MatchingPool pool, PoolExpected expected) =>
        pool.Lines
            .Where(l => l.Direction == expected.Direction && l.Currency == expected.Currency)
            .Select(l => Score(l, expected, ItemType.StatementLine, l.Id))
            .OrderByDescending(c => c.Score)
            .Take(CandidatesPerException)
            .ToList();

    /// <summary>Display score for exception candidates: amount closeness 0.5, date closeness 0.25 (30-day horizon), name 0.25.</summary>
    private static ExceptionCandidate Score(PoolLine line, PoolExpected expected, ItemType type, Guid id)
    {
        var amountDifference = line.Magnitude - expected.Outstanding;
        var largest = Math.Max(line.Magnitude, expected.Outstanding);
        var amountScore = largest == 0 ? 1m : 1m - Math.Min(1m, Math.Abs(amountDifference) / largest);
        var days = Math.Abs(line.ValueDate.DayNumber - expected.ExpectedDate.DayNumber);
        var dateScore = 1m - Math.Min(1m, days / 30m);
        var similarity = (decimal)JaroWinkler.Best(line.CounterpartyName, expected.CounterpartyNames);
        var referenceMatch = expected.CanonicalReference is { } reference && ExactReferenceRule.References(line).Contains(reference) ? 1m : 0m;
        var score = Math.Round((0.5m * amountScore) + (0.25m * dateScore) + (0.25m * similarity), 4);

        return new ExceptionCandidate(type, id, score, new Dictionary<string, decimal>
        {
            ["score"] = score,
            ["amountDifference"] = amountDifference,
            ["dateDiffDays"] = days,
            ["nameSimilarity"] = Math.Round(similarity, 4),
            ["referenceMatch"] = referenceMatch,
        });
    }
}
