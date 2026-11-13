using TreasuryKit.BuildingBlocks.Domain;
using TreasuryKit.Core.Domain.MappingProfiles;
using TreasuryKit.Core.Domain.Statements;

namespace TreasuryKit.Core.Application.Statements.Parsing;

public sealed record ParsedLine(
    int RowNumber,
    DateOnly BookingDate,
    DateOnly ValueDate,
    decimal Amount,
    string Narration,
    string? BankReference,
    decimal? RunningBalance,
    string? CurrencyCode,
    string? EndToEndId = null,
    string? CounterpartyName = null);

/// <summary>A parsed statement, lines in chronological order.</summary>
/// <param name="AccountIdentifier">The account as stated in the file (MT940 :25:, CAMT Acct/Id); null for CSV/XLSX.</param>
/// <param name="BalanceIssue">A balance problem the parser found itself, e.g. a gap between consecutive statements in one file.</param>
/// <param name="SkippedEntries">Entries deliberately not imported (e.g. CAMT pending entries).</param>
public sealed record ParsedStatement(
    string? AccountIdentifier,
    Currency? Currency,
    DateOnly? PeriodStart,
    DateOnly? PeriodEnd,
    decimal? OpeningBalance,
    decimal? ClosingBalance,
    IReadOnlyList<ParsedLine> Lines,
    string? BalanceIssue = null,
    int SkippedEntries = 0);

/// <summary>What a parser needs besides the bytes. <see cref="Profile"/> is required for CSV/XLSX.</summary>
public sealed record ParseContext(string FileName, BankMappingProfile? Profile);

public interface IStatementParser
{
    StatementFormat Format { get; }

    bool CanParse(ReadOnlySpan<byte> header, string fileName);

    Task<Result<ParsedStatement>> ParseAsync(Stream stream, ParseContext context, CancellationToken cancellationToken);
}

public interface IStatementParserResolver
{
    Result<IStatementParser> Resolve(StatementFormat format);

    /// <summary>Identifies the format from the first bytes of the file, then the extension.</summary>
    Result<StatementFormat> Detect(ReadOnlySpan<byte> header, string fileName);
}

/// <summary>Bulk writes to statement lines. Runs in the caller's transaction.</summary>
public interface IStatementLineWriter
{
    /// <summary>Inserts lines, skipping fingerprints that already exist. Returns the number actually inserted.</summary>
    Task<int> InsertAsync(IReadOnlyList<StatementLine> lines, CancellationToken cancellationToken);

    /// <summary>Overwrites normalized narration, extracted references and counterparty guess.</summary>
    Task UpdateAnalysisAsync(IReadOnlyList<LineAnalysis> analyses, CancellationToken cancellationToken);
}

public sealed record LineAnalysis(Guid LineId, string NormalizedNarration, string ExtractedRefs, string? CounterpartyNameGuess);

public static class ParseErrors
{
    public static Error Invalid(string message) => Error.Validation("Statement.ParseFailed", message);

    public static Error AtRow(int row, string message) => Invalid($"Row {row}: {message}");
}
