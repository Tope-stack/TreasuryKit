using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TreasuryKit.Core.Domain.Common;

namespace TreasuryKit.Core.Domain.Statements;

/// <summary>
/// Identifies a statement line across imports so overlapping statements don't create duplicates (plan §4.4):
/// SHA256(accountId | valueDate | amount | bankReference ?? narration | occurrenceIndex).
/// Narration uses the fixed <see cref="TextNormalization.ForFingerprint"/>; the format must never change (ADR 0005).
/// </summary>
public static class LineFingerprint
{
    public static string Compute(Guid accountId, DateOnly valueDate, decimal amount, string? bankReference, string narration, int occurrenceIndex)
    {
        var identity = string.IsNullOrWhiteSpace(bankReference)
            ? "N:" + TextNormalization.ForFingerprint(narration)
            : "R:" + TextNormalization.ForFingerprint(bankReference);

        // Explicit array: a params-span call would make the compiler emit a hidden InlineArray type into the Domain assembly.
        var input = string.Join('|', new[]
        {
            accountId.ToString("N"),
            valueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            amount.ToString("F4", CultureInfo.InvariantCulture),
            identity,
            occurrenceIndex.ToString(CultureInfo.InvariantCulture),
        });

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
    }

    /// <summary>
    /// Computes fingerprints for a file's lines in order. Identical (valueDate, amount, identity) tuples get
    /// increasing occurrence indexes, so two genuinely identical transactions on the same day both survive.
    /// </summary>
    public static IReadOnlyList<string> ComputeAll<TLine>(
        Guid accountId,
        IReadOnlyList<TLine> lines,
        Func<TLine, (DateOnly ValueDate, decimal Amount, string? BankReference, string Narration)> select)
    {
        var occurrences = new Dictionary<(DateOnly, decimal, string), int>();
        var result = new string[lines.Count];
        for (var i = 0; i < lines.Count; i++)
        {
            var (valueDate, amount, reference, narration) = select(lines[i]);
            var key = (valueDate, decimal.Round(amount, 4),
                string.IsNullOrWhiteSpace(reference) ? "N:" + TextNormalization.ForFingerprint(narration) : "R:" + TextNormalization.ForFingerprint(reference));
            var index = occurrences.GetValueOrDefault(key);
            occurrences[key] = index + 1;
            result[i] = Compute(accountId, valueDate, amount, reference, narration, index);
        }

        return result;
    }
}
