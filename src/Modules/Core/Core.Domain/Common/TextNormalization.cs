using System.Text;

namespace TreasuryKit.Core.Domain.Common;

public static class TextNormalization
{
    /// <summary>
    /// Fixed normalization used in line fingerprints: trim, upper-case (invariant), collapse whitespace.
    /// Must never change — fingerprints of stored lines depend on it (ADR 0005).
    /// </summary>
    public static string ForFingerprint(string? text) => CollapseWhitespace((text ?? string.Empty).ToUpperInvariant());

    /// <summary>Name normalization for counterparties: upper-case, punctuation to spaces, collapse whitespace.</summary>
    public static string ForName(string? text)
    {
        var builder = new StringBuilder(text?.Length ?? 0);
        foreach (var c in (text ?? string.Empty).ToUpperInvariant())
        {
            builder.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }

        return CollapseWhitespace(builder.ToString());
    }

    private static string CollapseWhitespace(string text)
    {
        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }
}
