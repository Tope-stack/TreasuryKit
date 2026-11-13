using TreasuryKit.Recon.Domain.Matching;

namespace TreasuryKit.Recon.Domain.Rules;

public interface IMatchingRule
{
    string Key { get; }

    /// <summary>Proposes candidates from the current pool. Rules may flag items (ambiguity, currency) on the pool.</summary>
    IEnumerable<MatchCandidate> Find(MatchingPool pool, RuleParameters parameters);
}

/// <summary>
/// A reference on the line (NIP session id, invoice ref, payment ref, bank reference, end-to-end id) equals the expected
/// transaction's canonical reference, and amount and direction agree. Confidence 1.0 (plan §5.3).
/// </summary>
public sealed class ExactReferenceRule : IMatchingRule
{
    public string Key => RuleKeys.ExactReference;

    public IEnumerable<MatchCandidate> Find(MatchingPool pool, RuleParameters parameters)
    {
        var byReference = pool.Expected
            .Where(e => e.CanonicalReference is { Length: > 0 })
            .ToLookup(e => e.CanonicalReference!, StringComparer.Ordinal);
        if (byReference.Count == 0)
        {
            yield break;
        }

        foreach (var line in pool.Lines)
        {
            foreach (var expected in References(line).SelectMany(r => byReference[r]).Distinct())
            {
                if (expected.Direction != line.Direction || expected.Outstanding != line.Magnitude)
                {
                    continue; // same reference, different amount: a split or a deduction (Phase 4 rules)
                }

                if (!string.Equals(expected.Currency, line.Currency, StringComparison.Ordinal))
                {
                    pool.Flag(ItemType.StatementLine, line.Id, ExceptionReason.CurrencyMismatch);
                    pool.Flag(ItemType.ExpectedTransaction, expected.Id, ExceptionReason.CurrencyMismatch);
                    continue;
                }

                yield return new MatchCandidate(
                    Key,
                    MatchType.OneToOne,
                    [new ItemRef(ItemType.StatementLine, line.Id, line.Magnitude)],
                    [new ItemRef(ItemType.ExpectedTransaction, expected.Id, line.Magnitude)],
                    1.0m,
                    new Dictionary<string, decimal> { ["reference"] = 1.0m, ["amount"] = 1.0m });
            }
        }
    }

    /// <summary>All canonical references a line carries.</summary>
    internal static IEnumerable<string> References(PoolLine line)
    {
        var refs = new HashSet<string>(StringComparer.Ordinal);
        void Add(string? value)
        {
            var canonical = Canonical(value);
            if (canonical.Length > 0)
            {
                refs.Add(canonical);
            }
        }

        Add(line.NipSessionId);
        Add(line.BankReference);
        Add(line.EndToEndId);
        foreach (var r in line.InvoiceRefs)
        {
            Add(r);
        }

        foreach (var r in line.PaymentRefs)
        {
            Add(r);
        }

        return refs;
    }

    /// <summary>Same canonical form as Core (ADR 0006): upper-case letters and digits only.</summary>
    public static string Canonical(string? value) =>
        value is null ? string.Empty : new string(value.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
}

/// <summary>
/// Equal amount, same direction, value date within the window, and exactly one candidate on each side.
/// Score = 0.80 + date proximity (≤ 0.10) + counterparty name similarity (≤ 0.10, only above the minimum) (plan §5.3).
/// </summary>
public sealed class AmountDateWindowRule : IMatchingRule
{
    public const decimal BaseScore = 0.80m;

    public string Key => RuleKeys.AmountDateWindow;

    public IEnumerable<MatchCandidate> Find(MatchingPool pool, RuleParameters parameters)
    {
        var window = (int)parameters.Get("windowDays", 3m);
        var nameMinimum = (double)parameters.Get("nameSimilarityMin", 0.85m);

        bool Fits(PoolLine line, PoolExpected expected) =>
            expected.Direction == line.Direction
            && expected.Outstanding == line.Magnitude
            && string.Equals(expected.Currency, line.Currency, StringComparison.Ordinal)
            && Math.Abs(line.ValueDate.DayNumber - expected.ExpectedDate.DayNumber) <= window;

        var expectedByAmount = pool.Expected.ToLookup(e => e.Outstanding);
        var linesByAmount = pool.Lines.ToLookup(l => l.Magnitude);

        foreach (var line in pool.Lines)
        {
            var forLine = expectedByAmount[line.Magnitude].Where(e => Fits(line, e)).ToList();
            if (forLine.Count == 0)
            {
                continue;
            }

            if (forLine.Count > 1)
            {
                pool.Flag(ItemType.StatementLine, line.Id, ExceptionReason.MultipleCandidates);
                forLine.ForEach(e => pool.Flag(ItemType.ExpectedTransaction, e.Id, ExceptionReason.MultipleCandidates));
                continue;
            }

            var expected = forLine[0];
            var forExpected = linesByAmount[expected.Outstanding].Count(l => Fits(l, expected));
            if (forExpected > 1)
            {
                pool.Flag(ItemType.StatementLine, line.Id, ExceptionReason.MultipleCandidates);
                pool.Flag(ItemType.ExpectedTransaction, expected.Id, ExceptionReason.MultipleCandidates);
                continue;
            }

            var days = Math.Abs(line.ValueDate.DayNumber - expected.ExpectedDate.DayNumber);
            var dateScore = Round(0.10m * (1m - (days / (decimal)(window + 1))));
            var similarity = JaroWinkler.Best(line.CounterpartyName, expected.CounterpartyNames);
            var nameScore = similarity >= nameMinimum ? Round(0.10m * (decimal)similarity) : 0m;

            yield return new MatchCandidate(
                Key,
                MatchType.OneToOne,
                [new ItemRef(ItemType.StatementLine, line.Id, line.Magnitude)],
                [new ItemRef(ItemType.ExpectedTransaction, expected.Id, line.Magnitude)],
                BaseScore + dateScore + nameScore,
                new Dictionary<string, decimal>
                {
                    ["base"] = BaseScore,
                    ["date"] = dateScore,
                    ["name"] = nameScore,
                    ["dateDiffDays"] = days,
                    ["nameSimilarity"] = Round((decimal)similarity),
                });
        }
    }

    private static decimal Round(decimal value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);
}
