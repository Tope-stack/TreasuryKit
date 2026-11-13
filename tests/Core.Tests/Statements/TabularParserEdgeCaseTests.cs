using System.Globalization;
using System.Text;
using TreasuryKit.BuildingBlocks.Domain;
using TreasuryKit.Core.Application.Statements.Parsing;
using TreasuryKit.Core.Infrastructure.Parsing;

namespace TreasuryKit.Core.Tests.Statements;

public sealed class TabularParserEdgeCaseTests
{
    private const string Header = "Trans Date,Value Date,Reference,Description,Amount,Balance\n";

    [Theory]
    [InlineData("1,234.56", 1234.56)]
    [InlineData("-1,234.56", -1234.56)]
    [InlineData("(1,234.56)", -1234.56)]
    [InlineData("1,234.56 DR", -1234.56)]
    [InlineData("1,234.56CR", 1234.56)]
    [InlineData("₦ 1,234.56", 1234.56)]
    [InlineData("NGN1,234.56", 1234.56)]
    [InlineData(" 1 234.56 ", 1234.56)]
    [InlineData("0.00", 0)]
    public void Parses_amount_formats(string text, double expected)
    {
        Assert.True(TabularStatementMapper.TryAmount(text, CultureInfo.GetCultureInfo("en-NG"), out var amount));
        Assert.Equal((decimal)expected, amount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("-")]
    [InlineData(null)]
    public void Blank_amount_is_null(string? text)
    {
        Assert.True(TabularStatementMapper.TryAmount(text, CultureInfo.InvariantCulture, out var amount));
        Assert.Null(amount);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("12..3")]
    public void Rejects_garbage_amounts(string text) =>
        Assert.False(TabularStatementMapper.TryAmount(text, CultureInfo.InvariantCulture, out _));

    [Fact]
    public async Task Skips_blank_rows_and_repeated_headers_and_stops_at_footer()
    {
        var csv = Header +
            "01/01/2026,01/01/2026,R1,FIRST,\"100.00\",\"1,100.00\"\n" +
            ",,,,,\n" +
            Header +
            "02/01/2026,02/01/2026,R2,SECOND,\"-50.00\",\"1,050.00\"\n" +
            "Closing Balance,,,,,\"1,050.00\"\n" +
            "Total,,,,,\n";

        var result = await ParseAsync(csv);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(["FIRST", "SECOND"], result.Value.Lines.Select(l => l.Narration));
        Assert.Equal(1_000.00m, result.Value.OpeningBalance);
        Assert.Equal(1_050.00m, result.Value.ClosingBalance);
    }

    [Fact]
    public async Task Bad_date_reports_row_number()
    {
        var result = await ParseAsync(Header + "01/01/2026,01/01/2026,R1,A,1.00,1.00\n2026-13-45,,R2,B,1.00,2.00\n");

        Assert.True(result.IsFailure);
        Assert.StartsWith("Row 3:", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Debit_and_credit_on_same_row_is_rejected()
    {
        var csv = "Txn Date,Remarks,Ref No,Withdrawals,Deposits,Running Balance\n05-Jan-2026,X,R1,10.00,20.00,100.00\n";

        var result = await new CsvStatementParser().ParseAsync(
            Stream(csv), new ParseContext("x.csv", TestProfiles.LayoutC), TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Contains("both a debit and a credit", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_value_date_falls_back_to_booking_date()
    {
        var result = await ParseAsync(Header + "03/01/2026,,R1,A,\"1.00\",\"1.00\"\n");

        Assert.Equal(new DateOnly(2026, 1, 3), result.Value.Lines[0].ValueDate);
    }

    [Fact]
    public async Task Same_day_newest_first_is_reordered_by_balance_chain()
    {
        var csv = Header +
            "05/01/2026,05/01/2026,R3,THIRD,\"30.00\",\"160.00\"\n" +
            "05/01/2026,05/01/2026,R2,SECOND,\"20.00\",\"130.00\"\n" +
            "05/01/2026,05/01/2026,R1,FIRST,\"10.00\",\"110.00\"\n";

        var result = await ParseAsync(csv);

        Assert.Equal(["FIRST", "SECOND", "THIRD"], result.Value.Lines.Select(l => l.Narration));
        Assert.Equal(100.00m, result.Value.OpeningBalance);
    }

    private static Task<Result<ParsedStatement>> ParseAsync(string csv) =>
        new CsvStatementParser().ParseAsync(Stream(csv), new ParseContext("x.csv", TestProfiles.LayoutA), TestContext.Current.CancellationToken);

    private static MemoryStream Stream(string text) => new(Encoding.UTF8.GetBytes(text));
}
