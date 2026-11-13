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

internal sealed record Txn(DateOnly BookingDate, DateOnly ValueDate, string Narration, string? Reference, decimal Amount, decimal Balance);

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
    int OverlapWithPrevious = 0);

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
        var raw = new List<(DateOnly Booking, DateOnly Value, string Narration, string? Reference, decimal Amount)>();
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
                    raw.Add((day, valueDate, $"NIP/FRM {customer}/{Session(random, day)}/INV-{++invoice}", NextRef(), amount));
                }
                else if (kind < 7)
                {
                    var supplier = Suppliers[random.Next(Suppliers.Length)];
                    raw.Add((day, day, $"NIP TRF TO {supplier}/{Session(random, day)}/PO-{++po}", NextRef(), -Money(random, 80_000, 1_500_000)));
                }
                else if (kind < 9)
                {
                    raw.Add((day, day, $"POS/WEB PURCHASE {Merchants[random.Next(Merchants.Length)]}", NextRef(), -Money(random, 5_000, 150_000)));
                }
                else
                {
                    raw.Add((day, day, $"TRF FRM {Customers[random.Next(Customers.Length)]} REF {random.Next(100000, 999999)}", NextRef(), Money(random, 20_000, 400_000)));
                }
            }
        }

        // Two genuinely identical charges on the same day, without a reference: exercises the fingerprint occurrence index.
        var chargeDay = new DateOnly(2026, 1, 15);
        raw.Add((chargeDay, chargeDay, "SMS ALERT CHARGES", null, -4.00m));
        raw.Add((chargeDay, chargeDay, "SMS ALERT CHARGES", null, -4.00m));

        // Month-end charges.
        raw.Add((to.AddDays(-1), to.AddDays(-1), "COMMISSION ON TURNOVER", null, -2_500.00m));
        raw.Add((to.AddDays(-1), to.AddDays(-1), "VAT ON COMM", null, -187.50m));

        var balance = openingBalance;
        var lines = raw
            .OrderBy(t => t.Booking)
            .Select(t =>
            {
                balance += t.Amount;
                return new Txn(t.Booking, t.Value, t.Narration, t.Reference, t.Amount, balance);
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
