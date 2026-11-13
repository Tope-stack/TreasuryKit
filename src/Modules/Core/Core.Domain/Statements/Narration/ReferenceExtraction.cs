using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using TreasuryKit.BuildingBlocks.Domain;
using TreasuryKit.Core.Domain.Common;

namespace TreasuryKit.Core.Domain.Statements.Narration;

/// <summary>References found on a statement line; serialized to <c>statement_lines.extracted_refs</c>.</summary>
/// <param name="InvoiceRefs">Canonical form: upper-case letters and digits only ("INV-1041" → "INV1041").</param>
public sealed record ExtractedReferences(string? NipSessionId, IReadOnlyList<string> InvoiceRefs, IReadOnlyList<string> PaymentRefs)
{
    public static readonly ExtractedReferences None = new(null, [], []);

    public static string Canonical(string reference) =>
        new(reference.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
}

/// <summary>Structured data some formats carry (CAMT.053), preferred over guessing from the narration.</summary>
public sealed record ExtractionHints(string? EndToEndId, string? CounterpartyName)
{
    public static readonly ExtractionHints None = new(null, null);
}

public sealed record ExtractionResult(ExtractedReferences References, string? CounterpartyNameGuess);

/// <summary>Mutable state passed through the ordered rules.</summary>
public sealed class ExtractionContext(string narration, ExtractionHints hints, InvoiceReferencePatterns invoicePatterns)
{
    /// <summary>Upper-cased narration with whitespace collapsed (punctuation kept: rules rely on separators).</summary>
    public string Narration { get; } = TextNormalization.ForFingerprint(narration);

    public ExtractionHints Hints { get; } = hints;

    public InvoiceReferencePatterns InvoicePatterns { get; } = invoicePatterns;

    public string? NipSessionId { get; set; }

    public List<string> InvoiceRefs { get; } = [];

    public List<string> PaymentRefs { get; } = [];

    public string? CounterpartyNameGuess { get; set; }
}

public interface IReferenceRule
{
    /// <summary>Rules run in ascending order; later rules can see what earlier ones found.</summary>
    int Order { get; }

    void Apply(ExtractionContext context);
}

public interface IReferenceExtractor
{
    ExtractionResult Extract(string? narration, ExtractionHints hints, InvoiceReferencePatterns invoicePatterns);
}

public sealed class ReferenceExtractor(IEnumerable<IReferenceRule> rules) : IReferenceExtractor
{
    private readonly IReferenceRule[] _rules = rules.OrderBy(r => r.Order).ToArray();

    /// <summary>The built-in rules from plan §4.3.</summary>
    public static ReferenceExtractor Default { get; } = new(
        [new NipSessionIdRule(), new InvoiceReferenceRule(), new PaymentReferenceRule(), new CounterpartyNameRule()]);

    public ExtractionResult Extract(string? narration, ExtractionHints hints, InvoiceReferencePatterns invoicePatterns)
    {
        var context = new ExtractionContext(narration ?? string.Empty, hints, invoicePatterns);
        foreach (var rule in _rules)
        {
            rule.Apply(context);
        }

        return new ExtractionResult(
            new ExtractedReferences(context.NipSessionId, context.InvoiceRefs.Distinct().ToList(), context.PaymentRefs.Distinct().ToList()),
            context.CounterpartyNameGuess);
    }
}

/// <summary>
/// NIBSS Instant Payment session id: exactly 30 digits (6-digit institution code + yyMMddHHmmss + 12 digits),
/// not part of a longer number. With several candidates, the one with a valid timestamp wins.
/// </summary>
public sealed partial class NipSessionIdRule : IReferenceRule
{
    public int Order => 10;

    public void Apply(ExtractionContext context)
    {
        var candidates = ThirtyDigits().Matches(context.Narration).Select(m => m.Value).ToList();
        context.NipSessionId = candidates.FirstOrDefault(HasValidTimestamp) ?? candidates.FirstOrDefault();
    }

    internal static bool HasValidTimestamp(string sessionId) =>
        DateTime.TryParseExact(sessionId.Substring(6, 12), "yyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    [GeneratedRegex(@"(?<!\d)\d{30}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex ThirtyDigits();
}

/// <summary>Invoice references from the tenant's patterns (default <c>INV[-/ ]?\d+</c>), stored in canonical form.</summary>
public sealed class InvoiceReferenceRule : IReferenceRule
{
    public int Order => 20;

    public void Apply(ExtractionContext context)
    {
        foreach (var regex in context.InvoicePatterns.Compiled)
        {
            try
            {
                context.InvoiceRefs.AddRange(regex.Matches(context.Narration).Select(m => ExtractedReferences.Canonical(m.Value)));
            }
            catch (RegexMatchTimeoutException)
            {
                // A pathological pattern must not stall imports; the pattern simply yields nothing for this line.
            }
        }
    }
}

/// <summary>Payment references: UETRs (UUIDs), 16-character alphanumeric references, and CAMT end-to-end ids.</summary>
public sealed partial class PaymentReferenceRule : IReferenceRule
{
    public int Order => 30;

    public void Apply(ExtractionContext context)
    {
        if (context.Hints.EndToEndId is { Length: > 0 } endToEnd
            && !string.Equals(endToEnd, "NOTPROVIDED", StringComparison.OrdinalIgnoreCase))
        {
            context.PaymentRefs.Add(endToEnd.Trim().ToUpperInvariant());
        }

        context.PaymentRefs.AddRange(Uetr().Matches(context.Narration).Select(m => m.Value));

        foreach (var token in SixteenChars().Matches(context.Narration).Select(m => m.Value))
        {
            var hasLetter = token.Any(char.IsAsciiLetter);
            var hasDigit = token.Any(char.IsAsciiDigit);
            if (hasLetter && hasDigit && !context.InvoiceRefs.Contains(token))
            {
                context.PaymentRefs.Add(token);
            }
        }
    }

    [GeneratedRegex("[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}", RegexOptions.CultureInvariant)]
    private static partial Regex Uetr();

    [GeneratedRegex("(?<![A-Z0-9-])[A-Z0-9]{16}(?![A-Z0-9-])", RegexOptions.CultureInvariant)]
    private static partial Regex SixteenChars();
}

/// <summary>Counterparty from structured data, else the words after FRM/FROM/TO up to a separator or reference.</summary>
public sealed partial class CounterpartyNameRule : IReferenceRule
{
    private static readonly HashSet<string> NotNames = ["SELF", "ACCOUNT", "ACCT", "A/C", "CUSTOMER", "BENEFICIARY"];

    public int Order => 40;

    public void Apply(ExtractionContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.Hints.CounterpartyName))
        {
            context.CounterpartyNameGuess = TextNormalization.ForName(context.Hints.CounterpartyName);
            return;
        }

        foreach (Match match in AfterDirectionWord().Matches(context.Narration))
        {
            var name = TextNormalization.ForName(match.Groups["name"].Value);
            if (name.Count(char.IsAsciiLetter) >= 2 && !NotNames.Contains(name))
            {
                context.CounterpartyNameGuess = name.Length > 200 ? name[..200] : name;
                return;
            }
        }
    }

    // Name = letters/digits/&.,'- and spaces; stops at '/', '|', ':', a REF/INV keyword, or a long digit run.
    [GeneratedRegex(@"\b(?:FRM|FROM|TO)\b[\s:/-]*(?<name>[A-Z][A-Z0-9&.,' -]*?)\s*(?=$|[/|:]|\bREF\b|\bINV\b|\d{6,})", RegexOptions.CultureInvariant)]
    private static partial Regex AfterDirectionWord();
}

/// <summary>A tenant's invoice-reference regular expressions, validated and compiled with a match timeout.</summary>
public sealed class InvoiceReferencePatterns
{
    public const int MaxPatterns = 10;
    public const int MaxPatternLength = 200;
    public const string DefaultPattern = @"INV[-/ ]?\d+";

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly ConcurrentDictionary<string, Regex> Cache = new(StringComparer.Ordinal);

    private InvoiceReferencePatterns(IReadOnlyList<string> patterns)
    {
        Patterns = patterns;
        Compiled = patterns.Select(p => Cache.GetOrAdd(p, Compile)).ToArray();
    }

    public static InvoiceReferencePatterns Default { get; } = new([DefaultPattern]);

    public IReadOnlyList<string> Patterns { get; }

    public IReadOnlyList<Regex> Compiled { get; }

    public static Result<InvoiceReferencePatterns> Create(IEnumerable<string>? patterns)
    {
        var list = (patterns ?? []).Select(p => p?.Trim() ?? string.Empty).Where(p => p.Length > 0).Distinct().ToList();
        if (list.Count > MaxPatterns)
        {
            return Invalid($"at most {MaxPatterns} patterns are allowed");
        }

        foreach (var pattern in list)
        {
            if (pattern.Length > MaxPatternLength)
            {
                return Invalid($"pattern '{pattern[..20]}…' is longer than {MaxPatternLength} characters");
            }

            try
            {
                _ = Compile(pattern);
            }
            catch (ArgumentException ex)
            {
                return Invalid($"'{pattern}' is not a valid regular expression: {ex.Message}");
            }
        }

        return new InvoiceReferencePatterns(list);
    }

    private static Regex Compile(string pattern) =>
        new(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout);

    private static Error Invalid(string reason) => Error.Validation("ReferencePatterns.Invalid", $"Invalid invoice reference patterns: {reason}.");
}
