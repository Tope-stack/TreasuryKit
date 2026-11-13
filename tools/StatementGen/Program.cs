using System.Globalization;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;

// Deterministic generator for synthetic bank statements (plan §7). No real customer data.
//   dotnet run --project tools/StatementGen -- --out tests/TestData/statements [--seed 20260101]
var outDir = ArgValue(args, "--out") ?? "tests/TestData/statements";
var seed = int.Parse(ArgValue(args, "--seed") ?? "20260101", CultureInfo.InvariantCulture);

var month = TransactionStream.Generate(new Random(seed), new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), openingBalance: 5_000_000.00m);
var manifest = new Dictionary<string, FileManifest>();

Directory.CreateDirectory(Path.Combine(outDir, "layout-a"));
Directory.CreateDirectory(Path.Combine(outDir, "layout-b"));
Directory.CreateDirectory(Path.Combine(outDir, "layout-c"));

// Layout A: CSV, signed amount, running balance, dd/MM/yyyy, oldest first.
Write("layout-a/clean.csv", LayoutA.Csv(month.Lines), month.Describe(month.Lines));
var part1 = month.Lines.Where(l => l.BookingDate <= new DateOnly(2026, 1, 20)).ToList();
var part2 = month.Lines.Where(l => l.BookingDate >= new DateOnly(2026, 1, 14)).ToList();
Write("layout-a/overlap-part1.csv", LayoutA.Csv(part1), month.Describe(part1));
Write("layout-a/overlap-part2.csv", LayoutA.Csv(part2), month.Describe(part2) with
{
    OverlapWithPrevious = part2.Count(l => l.BookingDate <= new DateOnly(2026, 1, 20)),
});
Write("layout-a/unbalanced.csv", LayoutA.Csv(month.Lines, corruptBalanceAt: month.Lines.Count / 2), month.Describe(month.Lines) with { Balanced = false });

// Layout B: XLSX, debit/credit columns, opening/closing balance rows around the table.
WriteXlsx("layout-b/clean.xlsx", LayoutB.Workbook(month.Lines, month.OpeningBalance, month.ClosingBalance), month.Describe(month.Lines));
WriteXlsx("layout-b/unbalanced.xlsx", LayoutB.Workbook(month.Lines, month.OpeningBalance, month.ClosingBalance + 1_000.00m),
    month.Describe(month.Lines) with { Balanced = false });

// Layout C: CSV, debit/credit columns, dd-MMM-yyyy, newest first, no value date column.
Write("layout-c/clean.csv", LayoutC.Csv(month.Lines), month.Describe(month.Lines) with { NewestFirst = true });

// MT940: the same month as one statement (with :86: wrapped at 65 characters), as daily statements, and broken variants.
Directory.CreateDirectory(Path.Combine(outDir, "mt940"));
const string AccountNumber = "0123456789";
Write("mt940/month.sta", Mt940.Statements([month.Lines], month.OpeningBalance, $"058/{AccountNumber}"), month.Describe(month.Lines) with { Account = $"058/{AccountNumber}" });
var firstWeek = month.Lines.Where(l => l.BookingDate <= new DateOnly(2026, 1, 9)).ToList();
var daily = firstWeek.GroupBy(l => l.BookingDate).Select(g => (IReadOnlyList<Txn>)g.ToList()).ToList();
Write("mt940/daily.sta", Mt940.Statements(daily, month.OpeningBalance, AccountNumber), month.Describe(firstWeek) with { Account = AccountNumber });
Write("mt940/daily-gap.sta", Mt940.Statements(daily, month.OpeningBalance, AccountNumber, gapAfterStatement: 1), month.Describe(firstWeek) with { Account = AccountNumber, Balanced = false });
Write("mt940/other-account.sta", Mt940.Statements([firstWeek], month.OpeningBalance, "9876543210"), month.Describe(firstWeek) with { Account = "9876543210" });

// CAMT.053.001.08: the same month; plus a small camt.053.001.02 file with a pending entry and a batch entry.
Directory.CreateDirectory(Path.Combine(outDir, "camt053"));
Write("camt053/month.xml", Camt053.Month(month.Lines, month.OpeningBalance, AccountNumber), month.Describe(month.Lines) with { Account = AccountNumber });
Write("camt053/batch-pending-v02.xml", Camt053.BatchAndPendingV02(AccountNumber), Camt053.BatchAndPendingManifest(AccountNumber));

// Answer key for reference extraction over every generated narration.
var answerKey = month.Lines
    .Select(l => new NarrationExpectation(l.Narration, l.NipSessionId, l.InvoiceRef is null ? [] : [l.InvoiceRef], l.Counterparty))
    .ToList();
File.WriteAllText(
    Path.Combine(outDir, "narration-answer-key.json"),
    JsonSerializer.Serialize(answerKey, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }) + "\n");

var manifestPath = Path.Combine(outDir, "manifest.json");
File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }) + "\n");
Console.WriteLine($"Wrote {manifest.Count} statements and {manifestPath}");
return 0;

void Write(string name, string content, FileManifest entry)
{
    File.WriteAllText(Path.Combine(outDir, name), content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    manifest[name] = entry;
}

void WriteXlsx(string name, XLWorkbook workbook, FileManifest entry)
{
    using (workbook)
    {
        workbook.SaveAs(Path.Combine(outDir, name));
    }

    manifest[name] = entry;
}

static string? ArgValue(string[] args, string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

internal sealed record Txn(DateOnly BookingDate, DateOnly ValueDate, string Narration, string? Reference, decimal Amount, decimal Balance)
{
    public string? NipSessionId { get; init; }

    public string? InvoiceRef { get; init; }

    public string? Counterparty { get; init; }
}

/// <summary>Expected facts about a generated file, asserted by tests.</summary>
internal sealed record FileManifest(
    int LineCount,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal OpeningBalance,
    decimal ClosingBalance,
    decimal SumOfAmounts,
    int NipSessionIds,
    bool Balanced = true,
    bool NewestFirst = false,
    int OverlapWithPrevious = 0,
    string? Account = null,
    int SkippedEntries = 0);

internal sealed record NarrationExpectation(string Narration, string? NipSessionId, IReadOnlyList<string> InvoiceRefs, string? Counterparty);

internal sealed class TransactionStream
{
    private static readonly string[] Customers =
    [
        "ADEBAYO TRADING LTD", "KANO AGRO SUPPLIES", "LEKKI PHARMA LTD", "OKONKWO AND SONS", "SAHARA LOGISTICS",
        "EKO FOODS PLC", "NNEWI AUTO PARTS", "JOS TIN MERCHANTS",
    ];

    private static readonly string[] Suppliers =
    [
        "SHENZHEN BRIGHT ELECTRONICS", "ARIBA CEMENT DISTRIBUTORS", "IKEJA OFFICE MART", "TEMA PORT CLEARING LTD",
        "ABUJA FLEET SERVICES",
    ];

    private static readonly string[] Merchants = ["SHOPRITE LEKKI", "TOTAL ENERGIES VI", "CHICKEN REPUBLIC YABA", "JUMIA ONLINE"];

    private TransactionStream(decimal openingBalance, List<Txn> lines)
    {
        OpeningBalance = openingBalance;
        Lines = lines;
    }

    public decimal OpeningBalance { get; }

    public List<Txn> Lines { get; }

    public decimal ClosingBalance => Lines[^1].Balance;

    public static TransactionStream Generate(Random random, DateOnly from, DateOnly to, decimal openingBalance)
    {
        var raw = new List<(DateOnly Booking, DateOnly Value, string Narration, string? Reference, decimal Amount, string? Session, string? Invoice, string? Party)>();
        var invoice = 1040;
        var po = 310;
        var reference = 0;
        string NextRef() => $"FT{from:yyMM}{++reference:D6}";

        for (var day = from; day <= to; day = day.AddDays(1))
        {
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }

            for (var n = random.Next(2, 5); n > 0; n--)
            {
                var kind = random.Next(10);
                if (kind < 4)
                {
                    var customer = Customers[random.Next(Customers.Length)];
                    var amount = Money(random, 50_000, 2_000_000);
                    var valueDate = random.Next(5) == 0 ? day.AddDays(1) : day;
                    var session = Session(random, day);
                    raw.Add((day, valueDate, $"NIP/FRM {customer}/{session}/INV-{++invoice}", NextRef(), amount, session, $"INV{invoice}", customer));
                }
                else if (kind < 7)
                {
                    var supplier = Suppliers[random.Next(Suppliers.Length)];
                    var session = Session(random, day);
                    raw.Add((day, day, $"NIP TRF TO {supplier}/{session}/PO-{++po}", NextRef(), -Money(random, 80_000, 1_500_000), session, null, supplier));
                }
                else if (kind < 9)
                {
                    raw.Add((day, day, $"POS/WEB PURCHASE {Merchants[random.Next(Merchants.Length)]}", NextRef(), -Money(random, 5_000, 150_000), null, null, null));
                }
                else
                {
                    var payer = Customers[random.Next(Customers.Length)];
                    raw.Add((day, day, $"TRF FRM {payer} REF {random.Next(100000, 999999)}", NextRef(), Money(random, 20_000, 400_000), null, null, payer));
                }
            }
        }

        // Two genuinely identical charges on the same day, without a reference: exercises the fingerprint occurrence index.
        var chargeDay = new DateOnly(2026, 1, 15);
        raw.Add((chargeDay, chargeDay, "SMS ALERT CHARGES", null, -4.00m, null, null, null));
        raw.Add((chargeDay, chargeDay, "SMS ALERT CHARGES", null, -4.00m, null, null, null));

        // Month-end charges.
        raw.Add((to.AddDays(-1), to.AddDays(-1), "COMMISSION ON TURNOVER", null, -2_500.00m, null, null, null));
        raw.Add((to.AddDays(-1), to.AddDays(-1), "VAT ON COMM", null, -187.50m, null, null, null));

        var balance = openingBalance;
        var lines = raw
            .OrderBy(t => t.Booking)
            .Select(t =>
            {
                balance += t.Amount;
                return new Txn(t.Booking, t.Value, t.Narration, t.Reference, t.Amount, balance)
                {
                    NipSessionId = t.Session,
                    InvoiceRef = t.Invoice,
                    Counterparty = t.Party,
                };
            })
            .ToList();

        return new TransactionStream(openingBalance, lines);
    }

    public FileManifest Describe(IReadOnlyList<Txn> lines) =>
        new(
            lines.Count,
            lines.Min(l => l.BookingDate),
            lines.Max(l => l.BookingDate),
            lines[0].Balance - lines[0].Amount,
            lines[^1].Balance,
            lines.Sum(l => l.Amount),
            lines.Count(l => l.Narration.StartsWith("NIP", StringComparison.Ordinal)));

    private static decimal Money(Random random, int min, int max) => Math.Round(random.Next(min, max) + random.Next(100) / 100m, 2);

    /// <summary>A 30-digit NIP session id: sending bank code (6) + yyMMddHHmmss (12) + 12 random digits.</summary>
    private static string Session(Random random, DateOnly day)
    {
        var bank = $"{random.Next(0, 1_000_000):D6}";
        var stamp = $"{day:yyMMdd}{random.Next(8, 18):D2}{random.Next(0, 60):D2}{random.Next(0, 60):D2}";
        var tail = $"{random.Next(0, 1_000_000):D6}{random.Next(0, 1_000_000):D6}";
        return bank + stamp + tail;
    }
}

internal static class LayoutA
{
    public static string Csv(IReadOnlyList<Txn> lines, int? corruptBalanceAt = null)
    {
        var sb = new StringBuilder("Trans Date,Value Date,Reference,Description,Amount,Balance\n");
        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            var balance = i == corruptBalanceAt ? l.Balance + 100.00m : l.Balance;
            sb.Append(CultureInfo.InvariantCulture, $"{l.BookingDate:dd/MM/yyyy},{l.ValueDate:dd/MM/yyyy},{l.Reference},{Quote(l.Narration)},{Quote(Amount(l.Amount))},{Quote(Amount(balance))}\n");
        }

        return sb.ToString();
    }

    internal static string Amount(decimal value) => value.ToString("#,##0.00", CultureInfo.InvariantCulture);

    internal static string Quote(string value) => value.Contains(',') || value.Contains('"') ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
}

internal static class LayoutB
{
    public static XLWorkbook Workbook(IReadOnlyList<Txn> lines, decimal opening, decimal statedClosing)
    {
        var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Statement");
        sheet.Cell(1, 1).Value = "STATEMENT OF ACCOUNT";
        sheet.Cell(2, 1).Value = "Account Name:";
        sheet.Cell(2, 2).Value = "DEMO TRADING LTD";
        sheet.Cell(3, 1).Value = "Account Number:";
        sheet.Cell(3, 2).Value = "0123456789";
        sheet.Cell(4, 1).Value = "Currency:";
        sheet.Cell(4, 2).Value = "NGN";
        sheet.Cell(5, 1).Value = "Opening Balance:";
        sheet.Cell(5, 2).Value = opening;

        string[] headers = ["Post Date", "Value Date", "Narration", "Ref", "Debit", "Credit", "Balance"];
        for (var c = 0; c < headers.Length; c++)
        {
            sheet.Cell(7, c + 1).Value = headers[c];
        }

        var row = 8;
        foreach (var l in lines)
        {
            sheet.Cell(row, 1).Value = l.BookingDate.ToDateTime(TimeOnly.MinValue);
            sheet.Cell(row, 2).Value = l.ValueDate.ToDateTime(TimeOnly.MinValue);
            sheet.Cell(row, 3).Value = l.Narration;
            sheet.Cell(row, 4).Value = l.Reference ?? string.Empty;
            if (l.Amount < 0)
            {
                sheet.Cell(row, 5).Value = -l.Amount;
            }
            else
            {
                sheet.Cell(row, 6).Value = l.Amount;
            }

            sheet.Cell(row, 7).Value = l.Balance;
            row++;
        }

        sheet.Cell(row + 1, 1).Value = "Closing Balance:";
        sheet.Cell(row + 1, 2).Value = statedClosing;
        if (row > 8)
        {
            sheet.Range(8, 1, row - 1, 2).Style.DateFormat.Format = "dd/mm/yyyy";
        }
        return workbook;
    }
}

internal static class LayoutC
{
    public static string Csv(IReadOnlyList<Txn> lines)
    {
        var sb = new StringBuilder("Txn Date,Remarks,Ref No,Withdrawals,Deposits,Running Balance\n");
        foreach (var l in lines.Reverse())
        {
            var withdrawal = l.Amount < 0 ? (-l.Amount).ToString("0.00", CultureInfo.InvariantCulture) : string.Empty;
            var deposit = l.Amount > 0 ? l.Amount.ToString("0.00", CultureInfo.InvariantCulture) : string.Empty;
            var date = l.BookingDate.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture);
            sb.Append(CultureInfo.InvariantCulture, $"{date},{LayoutA.Quote(l.Narration)},{l.Reference},{withdrawal},{deposit},{l.Balance.ToString("0.00", CultureInfo.InvariantCulture)}\n");
        }

        return sb.ToString();
    }
}

internal static class Mt940
{
    private const int LineWidth = 65;

    /// <summary>One MT940 message per group of lines; balances chain unless <paramref name="gapAfterStatement"/> breaks them.</summary>
    public static string Statements(IReadOnlyList<IReadOnlyList<Txn>> statements, decimal openingOfFirst, string account, int? gapAfterStatement = null)
    {
        var sb = new StringBuilder();
        var opening = statements[0][0].Balance - statements[0][0].Amount;
        for (var i = 0; i < statements.Count; i++)
        {
            var lines = statements[i];
            var closing = opening + lines.Sum(l => l.Amount);
            sb.Append("{1:F01DEMONGLAXXXX0000000000}{2:O9400000260131DEMONGLAXXXX00000000002601310000N}{4:\n");
            sb.Append(CultureInfo.InvariantCulture, $":20:STMT{lines[0].BookingDate:yyMMdd}{i + 1:D2}\n");
            sb.Append(CultureInfo.InvariantCulture, $":25:{account}\n");
            sb.Append(CultureInfo.InvariantCulture, $":28C:{i + 1}/1\n");
            sb.Append(CultureInfo.InvariantCulture, $":{(i == 0 ? "60F" : "60M")}:{Balance(opening, lines[0].BookingDate.AddDays(-1))}\n");
            foreach (var l in lines)
            {
                var mark = l.Amount < 0 ? "D" : "C";
                var bankRef = l.Reference is null ? string.Empty : "//" + l.Reference;
                sb.Append(CultureInfo.InvariantCulture, $":61:{l.ValueDate:yyMMdd}{l.BookingDate:MMdd}{mark}{Amount(Math.Abs(l.Amount))}NTRFNONREF{bankRef}\n");
                sb.Append(":86:").Append(Wrap(l.Narration)).Append('\n');
            }

            sb.Append(CultureInfo.InvariantCulture, $":{(i == statements.Count - 1 ? "62F" : "62M")}:{Balance(closing, lines[^1].BookingDate)}\n");
            sb.Append("-}\n");
            opening = closing + (i + 1 == gapAfterStatement ? 10.00m : 0m);
        }

        return sb.ToString();
    }

    private static string Balance(decimal amount, DateOnly date) =>
        $"{(amount < 0 ? "D" : "C")}{date:yyMMdd}NGN{Amount(Math.Abs(amount))}";

    private static string Amount(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',');

    /// <summary>Hard wrap at 65 characters, mid-word — how many bank systems emit :86:.</summary>
    private static string Wrap(string text)
    {
        var parts = new List<string>();
        for (var i = 0; i < text.Length; i += LineWidth)
        {
            parts.Add(text.Substring(i, Math.Min(LineWidth, text.Length - i)));
        }

        return string.Join("\n", parts);
    }
}

internal static class Camt053
{
    private const string V08 = "urn:iso:std:iso:20022:tech:xsd:camt.053.001.08";
    private const string V02 = "urn:iso:std:iso:20022:tech:xsd:camt.053.001.02";

    public static string Month(IReadOnlyList<Txn> lines, decimal opening, string account)
    {
        var closing = lines[^1].Balance;
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <Document xmlns="{V08}">
              <BkToCstmrStmt>
                <GrpHdr><MsgId>MSG-2026-01</MsgId><CreDtTm>2026-02-01T06:00:00+01:00</CreDtTm></GrpHdr>
                <Stmt>
                  <Id>STMT-2026-01</Id>
                  <CreDtTm>2026-02-01T06:00:00+01:00</CreDtTm>
                  <Acct><Id><Othr><Id>{account}</Id></Othr></Id><Ccy>NGN</Ccy></Acct>
                  {Bal("OPBD", opening, new DateOnly(2025, 12, 31))}
                  {Bal("CLBD", closing, lines[^1].BookingDate)}

            """);
        foreach (var l in lines)
        {
            var credit = l.Amount >= 0;
            var party = l.Counterparty is null
                ? string.Empty
                : credit
                    ? $"<RltdPties><Dbtr><Pty><Nm>{Escape(l.Counterparty)}</Nm></Pty></Dbtr></RltdPties>"
                    : $"<RltdPties><Cdtr><Pty><Nm>{Escape(l.Counterparty)}</Nm></Pty></Cdtr></RltdPties>";
            var refs = l.Reference is null ? string.Empty : $"<Refs><EndToEndId>E2E{l.Reference}</EndToEndId></Refs>";
            sb.Append(CultureInfo.InvariantCulture, $"""
                      <Ntry>
                        <Amt Ccy="NGN">{Math.Abs(l.Amount).ToString("0.00", CultureInfo.InvariantCulture)}</Amt>
                        <CdtDbtInd>{(credit ? "CRDT" : "DBIT")}</CdtDbtInd>
                        <Sts><Cd>BOOK</Cd></Sts>
                        <BookgDt><Dt>{l.BookingDate:yyyy-MM-dd}</Dt></BookgDt>
                        <ValDt><Dt>{l.ValueDate:yyyy-MM-dd}</Dt></ValDt>
                        {(l.Reference is null ? string.Empty : $"<AcctSvcrRef>{l.Reference}</AcctSvcrRef>")}
                        <BkTxCd><Prtry><Cd>NTRF</Cd></Prtry></BkTxCd>
                        <NtryDtls><TxDtls>{refs}{party}<RmtInf><Ustrd>{Escape(l.Narration)}</Ustrd></RmtInf></TxDtls></NtryDtls>
                      </Ntry>

                """);
        }

        sb.Append("""
                </Stmt>
              </BkToCstmrStmt>
            </Document>

            """);
        return sb.ToString();
    }

    /// <summary>camt.053.001.02: one booked single entry, one booked batch (two transactions), one pending entry.</summary>
    public static string BatchAndPendingV02(string account) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <Document xmlns="{V02}">
          <BkToCstmrStmt>
            <GrpHdr><MsgId>MSG-B</MsgId><CreDtTm>2026-02-02T06:00:00</CreDtTm></GrpHdr>
            <Stmt>
              <Id>STMT-B</Id>
              <CreDtTm>2026-02-02T06:00:00</CreDtTm>
              <Acct><Id><Othr><Id>{account}</Id></Othr></Id><Ccy>NGN</Ccy></Acct>
              <Bal><Tp><CdOrPrtry><Cd>OPBD</Cd></CdOrPrtry></Tp><Amt Ccy="NGN">1000000.00</Amt><CdtDbtInd>CRDT</CdtDbtInd><Dt><Dt>2026-01-31</Dt></Dt></Bal>
              <Bal><Tp><CdOrPrtry><Cd>CLBD</Cd></CdOrPrtry></Tp><Amt Ccy="NGN">1150000.00</Amt><CdtDbtInd>CRDT</CdtDbtInd><Dt><Dt>2026-02-02</Dt></Dt></Bal>
              <Ntry>
                <Amt Ccy="NGN">250000.00</Amt><CdtDbtInd>CRDT</CdtDbtInd><Sts>BOOK</Sts>
                <BookgDt><DtTm>2026-02-02T09:15:00+01:00</DtTm></BookgDt><ValDt><Dt>2026-02-02</Dt></ValDt>
                <AcctSvcrRef>FT2602000001</AcctSvcrRef>
                <NtryDtls><TxDtls>
                  <Refs><EndToEndId>000013260202091500123456789012</EndToEndId></Refs>
                  <RltdPties><Dbtr><Nm>Okonkwo and Sons</Nm></Dbtr></RltdPties>
                  <RmtInf><Ustrd>PAYMENT FOR INV-7001</Ustrd></RmtInf>
                </TxDtls></NtryDtls>
              </Ntry>
              <Ntry>
                <Amt Ccy="NGN">100000.00</Amt><CdtDbtInd>DBIT</CdtDbtInd><Sts>BOOK</Sts>
                <BookgDt><Dt>2026-02-02</Dt></BookgDt><ValDt><Dt>2026-02-02</Dt></ValDt>
                <AcctSvcrRef>FT2602000002</AcctSvcrRef>
                <AddtlNtryInf>BULK SALARY PAYMENT FEB</AddtlNtryInf>
                <NtryDtls>
                  <TxDtls><Refs><EndToEndId>SAL-001</EndToEndId></Refs><RltdPties><Cdtr><Nm>STAFF ONE</Nm></Cdtr></RltdPties><RmtInf><Ustrd>SALARY FEB STAFF ONE</Ustrd></RmtInf></TxDtls>
                  <TxDtls><Refs><EndToEndId>SAL-002</EndToEndId></Refs><RltdPties><Cdtr><Nm>STAFF TWO</Nm></Cdtr></RltdPties><RmtInf><Ustrd>SALARY FEB STAFF TWO</Ustrd></RmtInf></TxDtls>
                </NtryDtls>
              </Ntry>
              <Ntry>
                <Amt Ccy="NGN">999.00</Amt><CdtDbtInd>CRDT</CdtDbtInd><Sts>PDNG</Sts>
                <BookgDt><Dt>2026-02-02</Dt></BookgDt><ValDt><Dt>2026-02-03</Dt></ValDt>
                <AddtlNtryInf>PENDING INWARD TRANSFER</AddtlNtryInf>
              </Ntry>
            </Stmt>
          </BkToCstmrStmt>
        </Document>

        """;

    public static FileManifest BatchAndPendingManifest(string account) =>
        new(2, new DateOnly(2026, 2, 2), new DateOnly(2026, 2, 2), 1_000_000.00m, 1_150_000.00m, 150_000.00m, 0,
            Account: account, SkippedEntries: 1);

    private static string Bal(string code, decimal amount, DateOnly date) =>
        $"<Bal><Tp><CdOrPrtry><Cd>{code}</Cd></CdOrPrtry></Tp><Amt Ccy=\"NGN\">{Math.Abs(amount).ToString("0.00", CultureInfo.InvariantCulture)}</Amt>" +
        $"<CdtDbtInd>{(amount < 0 ? "DBIT" : "CRDT")}</CdtDbtInd><Dt><Dt>{date:yyyy-MM-dd}</Dt></Dt></Bal>";

    private static string Escape(string text) => System.Security.SecurityElement.Escape(text);
}
