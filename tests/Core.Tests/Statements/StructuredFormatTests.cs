using System.Text;
using TreasuryKit.Core.Application.Statements;
using TreasuryKit.Core.Application.Statements.Parsing;
using TreasuryKit.Core.Domain.Statements;
using TreasuryKit.Core.Infrastructure.Parsing;

namespace TreasuryKit.Core.Tests.Statements;

public sealed class StructuredFormatTests
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Mt940_unwraps_86_lines_so_narrations_match_the_csv()
    {
        var csv = await GoldenParser.ParseAsync("layout-a/clean.csv");
        var mt940 = await GoldenParser.ParseAsync("mt940/month.sta");

        Assert.Equal(csv.Lines.Select(l => l.Narration), mt940.Lines.Select(l => l.Narration));
        Assert.Contains(mt940.Lines, l => l.Narration.Length > 65); // some were wrapped in the file
    }

    [Theory]
    [InlineData("67", "abc", "abc def")]
    public void Unwrap_joins_short_lines_with_a_space(string _, string first, string expected) =>
        Assert.Equal(expected, Mt940StatementParser.Unwrap(first + "\ndef"));

    [Fact]
    public void Unwrap_joins_full_width_lines_without_a_space()
    {
        var full = new string('A', 65);

        Assert.Equal(full + "BC", Mt940StatementParser.Unwrap(full + "\nBC"));
    }

    [Fact]
    public async Task Mt940_infers_booking_year_across_new_year_and_handles_reversals()
    {
        const string text = """
            :20:X1
            :25:0123456789
            :28C:1/1
            :60F:C251230NGN1000,00
            :61:2601021231C100,00NTRFNONREF//FT1
            :86:LATE CREDIT
            :61:260102RC40,00NTRFNONREF//FT2
            :86:REVERSAL OF CREDIT
            :61:260102RD15,50NMSCNONREF
            :86:REVERSAL OF DEBIT
            :62F:C260102NGN1075,50
            -
            """;

        var result = await new Mt940StatementParser().ParseAsync(Stream(text), new ParseContext("x.sta", null), _ct);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var lines = result.Value.Lines;
        Assert.Equal(new DateOnly(2025, 12, 31), lines[0].BookingDate);
        Assert.Equal(new DateOnly(2026, 1, 2), lines[0].ValueDate);
        Assert.Equal([100.00m, -40.00m, 15.50m], lines.Select(l => l.Amount));
        Assert.Equal(["FT1", "FT2", null], lines.Select(l => l.BankReference));
        Assert.Null(result.Value.BalanceIssue);
    }

    [Fact]
    public async Task Mt940_with_several_accounts_is_rejected()
    {
        const string text = ":20:A\n:25:111\n:60F:C260101NGN0,00\n:62F:C260101NGN0,00\n:20:B\n:25:222\n:60F:C260101NGN0,00\n:62F:C260101NGN0,00\n";

        var result = await new Mt940StatementParser().ParseAsync(Stream(text), new ParseContext("x.sta", null), _ct);

        Assert.True(result.IsFailure);
        Assert.Contains("several accounts", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mt940_gap_between_daily_statements_is_reported()
    {
        var statement = await GoldenParser.ParseAsync("mt940/daily-gap.sta");

        Assert.Contains("Statement 2 opens at", statement.BalanceIssue, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Camt_v02_skips_pending_entries_and_keeps_batches_as_one_line()
    {
        var statement = await GoldenParser.ParseAsync("camt053/batch-pending-v02.xml");

        Assert.Equal(1, statement.SkippedEntries);
        Assert.Equal(2, statement.Lines.Count);

        var single = statement.Lines.Single(l => l.Amount > 0);
        Assert.Equal("Okonkwo and Sons", single.CounterpartyName);
        Assert.Equal("000013260202091500123456789012", single.EndToEndId);
        Assert.Equal("FT2602000001", single.BankReference);
        Assert.Equal(new DateOnly(2026, 2, 2), single.BookingDate);

        var batch = statement.Lines.Single(l => l.Amount < 0);
        Assert.Null(batch.CounterpartyName);
        Assert.Null(batch.EndToEndId);
        Assert.StartsWith("BULK SALARY PAYMENT FEB", batch.Narration, StringComparison.Ordinal);
        Assert.Contains("STAFF TWO", batch.Narration, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Camt_with_a_dtd_is_rejected()
    {
        const string xml = """
            <?xml version="1.0"?>
            <!DOCTYPE Document [<!ENTITY x SYSTEM "file:///etc/passwd">]>
            <Document xmlns="urn:iso:std:iso:20022:tech:xsd:camt.053.001.02"><BkToCstmrStmt>&x;</BkToCstmrStmt></Document>
            """;

        var result = await new Camt053StatementParser().ParseAsync(Stream(xml), new ParseContext("x.xml", null), _ct);

        Assert.True(result.IsFailure);
        Assert.Contains("DTD", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("layout-a/clean.csv", StatementFormat.Csv)]
    [InlineData("layout-b/clean.xlsx", StatementFormat.Xlsx)]
    [InlineData("mt940/month.sta", StatementFormat.Mt940)]
    [InlineData("camt053/month.xml", StatementFormat.Camt053)]
    public async Task Resolver_detects_format_from_content(string file, StatementFormat expected)
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(GoldenFiles.Root, file), _ct);

        // Content wins over a misleading extension (.txt is common for MT940 exports).
        var renamed = expected is StatementFormat.Mt940 or StatementFormat.Camt053 ? "statement.txt" : file;
        Assert.Equal(expected, GoldenParser.Resolver.Detect(bytes.AsSpan(0, Math.Min(4096, bytes.Length)), renamed).Value);
    }

    [Fact]
    public void Resolver_rejects_unrecognised_content() =>
        Assert.True(GoldenParser.Resolver.Detect("hello world"u8, "notes.txt").IsFailure);

    [Theory]
    [InlineData("058/0123456789", "0123456789", true)]
    [InlineData("0123456789", "0123456789", true)]
    [InlineData("0123-456-789", "0123456789", true)]
    [InlineData("NG00DEMO0123456789", "0123456789", true)]
    [InlineData("9876543210", "0123456789", false)]
    [InlineData("01234567890", "0123456789", false)]
    public void Account_identifier_matching(string file, string account, bool matches) =>
        Assert.Equal(matches, AccountIdentifiers.Matches(file, account));

    private static MemoryStream Stream(string text) => new(Encoding.UTF8.GetBytes(text));
}
