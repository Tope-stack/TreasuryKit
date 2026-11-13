using System.Text.Json;
using TreasuryKit.Core.Domain.Statements.Narration;

namespace TreasuryKit.Core.Application.Statements;

public sealed record LineAnalysisResult(string NormalizedNarration, string ExtractedRefsJson, string? CounterpartyNameGuess);

/// <summary>Normalizes a narration and extracts references for storage on a statement line.</summary>
public sealed class LineAnalyzer(INarrationNormalizer normalizer, IReferenceExtractor extractor)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public LineAnalysisResult Analyze(string narration, string? endToEndId, string? counterpartyName, InvoiceReferencePatterns patterns)
    {
        var extraction = extractor.Extract(narration, new ExtractionHints(endToEndId, counterpartyName), patterns);
        return new LineAnalysisResult(
            normalizer.Normalize(narration),
            JsonSerializer.Serialize(extraction.References, Json),
            extraction.CounterpartyNameGuess);
    }
}

public static class AccountIdentifiers
{
    /// <summary>
    /// True when the account stated in a file is this account: equal after removing separators, or ending with the
    /// account number (e.g. "058/0123456789", or an IBAN whose tail is the domestic number).
    /// </summary>
    public static bool Matches(string fileIdentifier, string accountNumber)
    {
        var file = Clean(fileIdentifier);
        var account = Clean(accountNumber);
        return account.Length > 0 && (file == account || file.EndsWith(account, StringComparison.Ordinal));
    }

    private static string Clean(string value) => new(value.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
}
