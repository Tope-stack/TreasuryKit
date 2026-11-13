using TreasuryKit.Core.Application.Statements.Parsing;
using TreasuryKit.Core.Domain.Statements;
using TreasuryKit.Core.Infrastructure.Parsing;

namespace TreasuryKit.Core.Tests.Statements;

/// <summary>Parses every generated statement and checks it against the generator's manifest.</summary>
public sealed class TabularParserGoldenTests
{
    public static TheoryData<string> Files => new(GoldenFiles.Manifest.Keys.Order());

    [Theory]
    [MemberData(nameof(Files))]
    public async Task Parses_golden_file_to_expected_totals(string file)
    {
        var expected = GoldenFiles.Manifest[file];

        var statement = await ParseAsync(file);

        Assert.Equal(expected.LineCount, statement.Lines.Count);
        Assert.Equal(expected.PeriodStart, statement.PeriodStart);
        Assert.Equal(expected.PeriodEnd, statement.PeriodEnd);
        Assert.Equal(expected.OpeningBalance, statement.OpeningBalance);
        Assert.Equal(expected.SumOfAmounts, statement.Lines.Sum(l => l.Amount));
        Assert.Equal(expected.NipSessionIds, statement.Lines.Count(l => l.Narration.StartsWith("NIP", StringComparison.Ordinal)));
        Assert.True(
            statement.Lines.Zip(statement.Lines.Skip(1)).All(p => p.First.BookingDate <= p.Second.BookingDate),
            "lines must be in chronological order");
    }

    [Theory]
    [MemberData(nameof(Files))]
    public async Task Balance_check_matches_manifest(string file)
    {
        var statement = await ParseAsync(file);

        var check = BalanceCheck.Run(
            statement.OpeningBalance,
            statement.ClosingBalance,
            statement.Lines.Select(l => (l.RowNumber, l.Amount, l.RunningBalance)).ToList());

        Assert.Equal(
            GoldenFiles.Manifest[file].Balanced ? BalanceCheckOutcome.Balanced : BalanceCheckOutcome.Mismatch,
            check.Outcome);
    }

    [Fact]
    public async Task Newest_first_file_is_reordered_with_original_row_numbers()
    {
        var statement = await ParseAsync("layout-c/clean.csv");

        Assert.Equal(new DateOnly(2026, 1, 1), statement.Lines[0].BookingDate);
        Assert.Equal(statement.Lines.Count + 1, statement.Lines[0].RowNumber); // last row in the file
    }

    [Fact]
    public async Task Xlsx_reads_balances_from_rows_around_the_table()
    {
        var statement = await ParseAsync("layout-b/unbalanced.xlsx");
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

    private static async Task<ParsedStatement> ParseAsync(string file)
    {
        await using var stream = GoldenFiles.Open(file);
        IStatementParser parser = file.EndsWith(".xlsx", StringComparison.Ordinal) ? new XlsxStatementParser() : new CsvStatementParser();
        var result = await parser.ParseAsync(stream, new ParseContext(file, TestProfiles.For(file)), TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        return result.Value;
    }
}
