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
    string? CurrencyCode);

/// <summary>A parsed statement, lines in chronological order.</summary>
public sealed record ParsedStatement(
    string? AccountIdentifier,
    Currency? Currency,
    DateOnly? PeriodStart,
    DateOnly? PeriodEnd,
    decimal? OpeningBalance,
    decimal? ClosingBalance,
    IReadOnlyList<ParsedLine> Lines);

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
}

/// <summary>Bulk-inserts lines, skipping fingerprints that already exist. Runs in the caller's transaction.</summary>
public interface IStatementLineWriter
{
    /// <summary>Returns the number of lines actually inserted.</summary>
    Task<int> InsertAsync(IReadOnlyList<StatementLine> lines, CancellationToken cancellationToken);
}

public static class ParseErrors
{
    public static Error Invalid(string message) => Error.Validation("Statement.ParseFailed", message);

    public static Error AtRow(int row, string message) => Invalid($"Row {row}: {message}");
}
