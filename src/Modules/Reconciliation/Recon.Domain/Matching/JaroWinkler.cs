namespace TreasuryKit.Recon.Domain.Matching;

/// <summary>Jaro-Winkler similarity in [0, 1] (1 = identical), with the standard 0.1 prefix scale over up to 4 characters.</summary>
public static class JaroWinkler
{
    public static double Similarity(string? a, string? b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
        {
            return 0d;
        }

        if (string.Equals(a, b, StringComparison.Ordinal))
        {
            return 1d;
        }

        var window = Math.Max(0, (Math.Max(a.Length, b.Length) / 2) - 1);
        var aMatched = new bool[a.Length];
        var bMatched = new bool[b.Length];
        var matches = 0;

        for (var i = 0; i < a.Length; i++)
        {
            var start = Math.Max(0, i - window);
            var end = Math.Min(b.Length - 1, i + window);
            for (var j = start; j <= end; j++)
            {
                if (!bMatched[j] && a[i] == b[j])
                {
                    aMatched[i] = bMatched[j] = true;
                    matches++;
                    break;
                }
            }
        }

        if (matches == 0)
        {
            return 0d;
        }

        var transpositions = 0;
        for (int i = 0, k = 0; i < a.Length; i++)
        {
            if (!aMatched[i])
            {
                continue;
            }

            while (!bMatched[k])
            {
                k++;
            }

            if (a[i] != b[k])
            {
                transpositions++;
            }

            k++;
        }

        double m = matches;
        var jaro = ((m / a.Length) + (m / b.Length) + ((m - (transpositions / 2d)) / m)) / 3d;

        var prefix = 0;
        while (prefix < Math.Min(4, Math.Min(a.Length, b.Length)) && a[prefix] == b[prefix])
        {
            prefix++;
        }

        return jaro + (prefix * 0.1 * (1 - jaro));
    }

    /// <summary>Best similarity between a name and any of several names.</summary>
    public static double Best(string? name, IEnumerable<string> candidates) =>
        candidates.Select(c => Similarity(name, c)).DefaultIfEmpty(0d).Max();
}
