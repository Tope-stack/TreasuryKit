using TreasuryKit.Core.Application.Statements.Parsing;
using TreasuryKit.Core.Domain.Statements;
using TreasuryKit.Core.Infrastructure.Parsing;

namespace TreasuryKit.Core.Tests.Statements;

/// <summary>Detects and parses every generated statement (all formats) and checks it against the generator's manifest.</summary>
public sealed class StatementGoldenTests
{
    public static TheoryData<string> Files => new(GoldenFiles.Manifest.Keys.Order());

    [Theory]
    [MemberData(nameof(Files))]
    public async Task Parses_golden_file_to_expected_totals(string file)
    {
        var expected = GoldenFiles.Manifest[file];

        var statement = await GoldenParser.ParseAsync(file);

        Assert.Equal(expected.LineCount, statement.Lines.Count);
        Assert.Equal(expected.PeriodStart, statement.PeriodStart);
        Assert.Equal(expected.PeriodEnd, statement.PeriodEnd);
        Assert.Equal(expected.OpeningBalance, statement.OpeningBalance);
        Assert.Equal(expected.SumOfAmounts, statement.Lines.Sum(l => l.Amount));
        Assert.Equal(expected.NipSessionIds, statement.Lines.Count(l => l.Narration.StartsWith("NIP", StringComparison.Ordinal)));
        Assert.Equal(expected.Account, statement.AccountIdentifier);
        Assert.Equal(expected.SkippedEntries, statement.SkippedEntries);
        Assert.True(
            statement.Lines.Zip(statement.Lines.Skip(1)).All(p => p.First.BookingDate <= p.Second.BookingDate),
            "lines must be in chronological order");
    }

    [Theory]
    [MemberData(nameof(Files))]
    public async Task Balance_check_matches_manifest(string file)
    {
        var statement = await GoldenParser.ParseAsync(file);

        var check = statement.BalanceIssue is { } issue
            ? new BalanceCheck(BalanceCheckOutcome.Mismatch, issue)
            : BalanceCheck.Run(statement.OpeningBalance, statement.ClosingBalance, statement.Lines.Select(l => (l.RowNumber, l.Amount, l.RunningBalance)).ToList());

        Assert.Equal(GoldenFiles.Manifest[file].Balanced ? BalanceCheckOutcome.Balanced : BalanceCheckOutcome.Mismatch, check.Outcome);
    }

    [Fact]
    public async Task Newest_first_file_is_reordered_with_original_row_numbers()
    {
        var statement = await GoldenParser.ParseAsync("layout-c/clean.csv");

        Assert.Equal(new DateOnly(2026, 1, 1), statement.Lines[0].BookingDate);
        Assert.Equal(statement.Lines.Count + 1, statement.Lines[0].RowNumber); // last row in the file
    }

    [Fact]
    public async Task Xlsx_reads_balances_from_rows_around_the_table()
    {
        var statement = await GoldenParser.ParseAsync("layout-b/unbalanced.xlsx");
        var expected = GoldenFiles.Manifest["layout-b/unbalanced.xlsx"];

        Assert.Equal(expected.OpeningBalance, statement.OpeningBalance);
        Assert.Equal(expected.ClosingBalance + 1_000.00m, statement.ClosingBalance); // the generator overstates it
    }

    [Fact]
    public async Task Wrong_layout_reports_missing_columns()
    {
        await using var stream = GoldenFiles.Open("layout-c/clean.csv");

        var result = await new CsvStatementParser().ParseAsync(
            stream, new ParseContext("clean.csv", TestProfiles.LayoutA), TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Contains("missing columns", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("Trans Date", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Same_month_in_csv_mt940_and_camt_produces_identical_fingerprints()
    {
        var account = Guid.NewGuid();
        var csv = await GoldenParser.ParseAsync("layout-a/clean.csv");
        var mt940 = await GoldenParser.ParseAsync("mt940/month.sta");
        var camt = await GoldenParser.ParseAsync("camt053/month.xml");

        static IReadOnlyList<string> Fingerprints(Guid account, ParsedStatement s) =>
            LineFingerprint.ComputeAll(account, s.Lines, l => (l.ValueDate, l.Amount, l.BankReference, l.Narration));

        var expected = Fingerprints(account, csv).Order().ToList();
        Assert.Equal(expected, Fingerprints(account, mt940).Order().ToList());
        Assert.Equal(expected, Fingerprints(account, camt).Order().ToList());
    }
}

/// <summary>Parses a golden file the way the import pipeline does: detect the format, then parse.</summary>
internal static class GoldenParser
{
    public static readonly StatementParserResolver Resolver = new(
        [new CsvStatementParser(), new XlsxStatementParser(), new Mt940StatementParser(), new Camt053StatementParser()]);

    public static async Task<ParsedStatement> ParseAsync(string file)
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(GoldenFiles.Root, file), TestContext.Current.CancellationToken);
        var format = Resolver.Detect(bytes.AsSpan(0, Math.Min(bytes.Length, 4096)), file);
        Assert.True(format.IsSuccess, format.IsFailure ? format.Error.Message : null);

        using var stream = new MemoryStream(bytes);
        var result = await Resolver.Resolve(format.Value).Value
            .ParseAsync(stream, new ParseContext(file, TestProfiles.For(file)), TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        return result.Value;
    }
}
