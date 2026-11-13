using TreasuryKit.BuildingBlocks.Domain;
using TreasuryKit.Core.Application.Statements.Parsing;
using TreasuryKit.Core.Domain.Statements;

namespace TreasuryKit.Core.Infrastructure.Parsing;

/// <summary>CSV and XLSX statements read through the account's <see cref="Domain.MappingProfiles.BankMappingProfile"/>.</summary>
internal abstract class ProfileDrivenTabularParser : IStatementParser
{
    public abstract StatementFormat Format { get; }

    public abstract bool CanParse(ReadOnlySpan<byte> header, string fileName);

    public Task<Result<ParsedStatement>> ParseAsync(Stream stream, ParseContext context, CancellationToken cancellationToken)
    {
        if (context.Profile is null)
        {
            return Task.FromResult<Result<ParsedStatement>>(ParseErrors.Invalid("A mapping profile is required to read CSV/XLSX statements."));
        }

        try
        {
            var rows = ReadRows(stream, context);
            return Task.FromResult(TabularStatementMapper.Map(rows, context.Profile));
        }
        catch (Exception ex) when (ex is InvalidDataException or CsvHelper.CsvHelperException or FileFormatException
                                       or InvalidOperationException or ArgumentException)
        {
            return Task.FromResult<Result<ParsedStatement>>(ParseErrors.Invalid($"Cannot read {Format} file: {ex.Message}"));
        }
    }

    protected abstract IReadOnlyList<object?[]> ReadRows(Stream stream, ParseContext context);
}

internal sealed class CsvStatementParser : ProfileDrivenTabularParser
{
    public override StatementFormat Format => StatementFormat.Csv;

    public override bool CanParse(ReadOnlySpan<byte> header, string fileName) =>
        fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);

    protected override IReadOnlyList<object?[]> ReadRows(Stream stream, ParseContext context) =>
        TabularReaders.ReadCsv(stream, context.Profile!.Delimiter ?? ",");
}

internal sealed class XlsxStatementParser : ProfileDrivenTabularParser
{
    public override StatementFormat Format => StatementFormat.Xlsx;

    // XLSX is a ZIP container: "PK\x03\x04".
    public override bool CanParse(ReadOnlySpan<byte> header, string fileName) =>
        header.StartsWith("PK\u0003\u0004"u8) && fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase);

    protected override IReadOnlyList<object?[]> ReadRows(Stream stream, ParseContext context) =>
        TabularReaders.ReadXlsx(stream);
}

internal sealed class StatementParserResolver(IEnumerable<IStatementParser> parsers) : IStatementParserResolver
{
    private readonly Dictionary<StatementFormat, IStatementParser> _byFormat = parsers.ToDictionary(p => p.Format);

    public Result<IStatementParser> Resolve(StatementFormat format) =>
        _byFormat.TryGetValue(format, out var parser)
            ? Result.Success(parser)
            : Error.Validation("Statement.UnsupportedFormat", $"No parser for {format} statements yet.");
}
