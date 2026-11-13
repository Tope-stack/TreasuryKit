using System.Text;

namespace TreasuryKit.Core.Domain.Statements.Narration;

public interface INarrationNormalizer
{
    /// <summary>Canonical narration used for matching (never for line identity — see ADR 0005).</summary>
    string Normalize(string? narration);
}

/// <summary>
/// Upper-case, punctuation to spaces (keeping '&amp;' and in-word '-'), collapse whitespace, then strip leading
/// channel prefixes such as "NIP/", "TRF/", "FRM", "USSD/" repeatedly.
/// </summary>
public sealed class NarrationNormalizer : INarrationNormalizer
{
    public static readonly IReadOnlyList<string> DefaultPrefixes =
        ["NIP/", "NIP TRF", "NIP", "TRF/", "TRF", "FRM", "FROM", "TO", "MOB/", "MOB", "USSD/", "USSD", "POS/", "POS", "WEB/", "WEB"];

    private readonly string[][] _prefixes;

    public NarrationNormalizer(IEnumerable<string>? prefixes = null)
    {
        // Prefixes are normalized like narrations and matched as whole leading words, longest first.
        _prefixes = (prefixes ?? DefaultPrefixes)
            .Select(p => Clean(p).Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(words => words.Length > 0)
            .DistinctBy(words => string.Join(' ', words))
            .OrderByDescending(words => words.Length)
            .ToArray();
    }

    public string Normalize(string? narration)
    {
        var words = Clean(narration).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

        bool stripped;
        do
        {
            stripped = false;
            foreach (var prefix in _prefixes)
            {
                // Never strip the last word: "TRANSFER TO" stays meaningful, a bare "TO" does not become empty.
                if (words.Count > prefix.Length && words.Take(prefix.Length).SequenceEqual(prefix, StringComparer.Ordinal))
                {
                    words.RemoveRange(0, prefix.Length);
                    stripped = true;
                    break;
                }
            }
        }
        while (stripped);

        return string.Join(' ', words);
    }

    private static string Clean(string? text)
    {
        var source = (text ?? string.Empty).ToUpperInvariant();
        var builder = new StringBuilder(source.Length);
        for (var i = 0; i < source.Length; i++)
        {
            var c = source[i];
            // '&' is part of company names; '-' only inside a token ("INV-1041"), not as a separator.
            var keepHyphen = c == '-'
                && i > 0 && i < source.Length - 1
                && char.IsLetterOrDigit(source[i - 1]) && char.IsLetterOrDigit(source[i + 1]);
            builder.Append(char.IsLetterOrDigit(c) || c == '&' || keepHyphen ? c : ' ');
        }

        return builder.ToString();
    }
}
