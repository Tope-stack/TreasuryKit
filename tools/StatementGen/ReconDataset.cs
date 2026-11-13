using System.Globalization;
using System.Text;
using System.Text.Json;

/// <summary>
/// February 2026 statement + expected transactions with controlled reconciliation scenarios (plan §7) and an answer key
/// stating the true pairings and the outcome the Phase 3 engine should reach.
/// </summary>
internal static class ReconDataset
{
    public const string AccountNumber = "0123456789";

    private static readonly string[] Customers =
    [
        "ADEBAYO TRADING LTD", "KANO AGRO SUPPLIES LTD", "LEKKI PHARMA LTD", "OKONKWO AND SONS", "SAHARA LOGISTICS LTD",
        "EKO FOODS PLC", "NNEWI AUTO PARTS LTD", "JOS TIN MERCHANTS", "BENIN TIMBER WORKS LTD", "ABA TEXTILES LTD",
    ];

    private static readonly string[] Suppliers =
    [
        "SHENZHEN BRIGHT ELECTRONICS", "ARIBA CEMENT DISTRIBUTORS", "IKEJA OFFICE MART", "TEMA PORT CLEARING LTD", "ABUJA FLEET SERVICES",
    ];

    public static void Write(string outDir, Random random)
    {
        var builder = new Builder(random);

        Repeat(30, i => builder.CleanInvoice(i));
        Repeat(10, i => builder.CleanNipReference(i));
        Repeat(20, i => builder.CleanNoReference(i));
        foreach (var drift in new[] { 1, 1, 2, 2, 3, 3, 4, 4, 5, 5 })
        {
            builder.DateDrift(drift);
        }

        Repeat(5, i => builder.NoNameNoReference(i));
        Repeat(4, i => builder.Ambiguous(i));
        Repeat(4, i => builder.Split(i, parts: 2 + i)); // 2, 3, 4 and 5 parts
        Repeat(3, i => builder.BankChargeDeducted(i));
        Repeat(10, i => builder.UnmatchedLine(i));
        Repeat(8, i => builder.UnmatchedExpected(i));

        Directory.CreateDirectory(outDir);
        var lines = builder.StatementLines();
        File.WriteAllText(Path.Combine(outDir, "statement.csv"), LayoutA.Csv(lines), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(outDir, "expected.csv"), builder.ExpectedCsv(), new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(outDir, "answer-key.json"),
            JsonSerializer.Serialize(builder.Key, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }) + "\n");
        Console.WriteLine($"Wrote {lines.Count} statement lines, {builder.ExpectedCount} expected transactions, {builder.Key.Count} answer-key items to {outDir}");
    }

    private static void Repeat(int count, Action<int> action)
    {
        for (var i = 0; i < count; i++)
        {
            action(i);
        }
    }

    /// <param name="Outcome">What the engine should do: AutoConfirmed, Proposed or Unmatched. Splits and bank charges are proposed by the Phase 4 rules.</param>
    internal sealed record KeyItem(
        string Scenario,
        bool Clean,
        string Outcome,
        IReadOnlyList<string> ExternalIds,
        IReadOnlyList<string> BankReferences);

    private sealed record Expected(
        string ExternalId, bool AssignAccount, string Direction, decimal Amount, DateOnly Date, string? Counterparty, string? Reference, string Description);

    private sealed class Builder(Random random)
    {
        private static readonly DateOnly First = new(2026, 2, 2);
        private readonly List<(DateOnly Date, string Narration, string Reference, decimal Amount)> _lines = [];
        private readonly List<Expected> _expected = [];
        private readonly HashSet<decimal> _amounts = [];
        private int _ref;
        private int _ext;
        private int _invoice = 5000;

        public List<KeyItem> Key { get; } = [];

        public int ExpectedCount => _expected.Count;

        public void CleanInvoice(int i)
        {
            var customer = Customers[i % Customers.Length];
            var date = Day();
            var amount = UniqueAmount(50_000, 3_000_000);
            var invoice = $"INV-{++_invoice}";
            var line = Line(date, $"NIP/FRM {customer}/{Session(date)}/{invoice}", amount);
            var exp = Exp(true, "Inflow", amount, date.AddDays(random.Next(-2, 3)), Variant(customer), invoice, $"Invoice {invoice}");
            Key.Add(new("CleanInvoice", true, "AutoConfirmed", [exp], [line]));
        }

        public void CleanNipReference(int i)
        {
            var customer = Customers[(i + 3) % Customers.Length];
            var date = Day();
            var amount = UniqueAmount(20_000, 900_000);
            var session = Session(date);
            var line = Line(date, $"NIP/FRM {customer}/{session}", amount);
            var exp = Exp(i % 2 == 0, "Inflow", amount, date, customer, session, "Customer-supplied NIP session id");
            Key.Add(new("CleanNipReference", true, "AutoConfirmed", [exp], [line]));
        }

        public void CleanNoReference(int i)
        {
            var customer = Customers[(i + 5) % Customers.Length];
            var date = Day();
            var amount = UniqueAmount(10_000, 750_000);
            var line = Line(date, $"TRF FRM {customer} REF {random.Next(100000, 999999)}", amount);
            var exp = Exp(i % 2 == 1, "Inflow", amount, date, Variant(customer), null, "Expected receipt");
            Key.Add(new("CleanNoReference", true, "AutoConfirmed", [exp], [line]));
        }

        public void DateDrift(int days)
        {
            var supplier = Suppliers[random.Next(Suppliers.Length)];
            var date = Day(maxOffset: 20);
            var amount = UniqueAmount(80_000, 2_500_000);
            var line = Line(date, $"NIP TRF TO {supplier}/{Session(date)}/PO-{random.Next(100, 999)}", -amount);
            var exp = Exp(true, "Outflow", amount, date.AddDays(-days), supplier, null, "Supplier payment due");
            var outcome = days <= 2 ? "AutoConfirmed" : days == 3 ? "Proposed" : "Unmatched";
            Key.Add(new($"DateDrift{days}", false, outcome, [exp], [line]));
        }

        public void NoNameNoReference(int i)
        {
            var date = Day();
            var amount = UniqueAmount(5_000, 300_000);
            var line = Line(date, $"CASH DEPOSIT BRANCH {random.Next(100, 999)}", amount);
            var exp = Exp(true, "Inflow", amount, date, $"Walk-in customer {i + 1}", null, "Counter sale");
            Key.Add(new("NoNameNoReference", false, "Proposed", [exp], [line]));
        }

        public void Ambiguous(int i)
        {
            var date = Day();
            var amount = UniqueAmount(100_000, 400_000);
            var line = Line(date, $"TRANSFER RECEIVED {random.Next(1000, 9999)}", amount);
            var a = Exp(true, "Inflow", amount, date, Customers[i], null, "Expected receipt");
            var b = Exp(true, "Inflow", amount, date, Customers[i + 4], null, "Expected receipt");
            Key.Add(new("Ambiguous", false, "Unmatched", [a, b], [line]));
        }

        public void Split(int i, int parts)
        {
            var customer = Customers[(i + 7) % Customers.Length];
            var date = Day(maxOffset: 15);
            var invoice = $"INV-{++_invoice}";
            var total = 0m;
            var lines = new List<string>();
            for (var p = 1; p <= parts; p++)
            {
                var part = UniqueAmount(40_000, 600_000);
                total += part;
                lines.Add(Line(date.AddDays(p - 1), $"NIP/FRM {customer}/{Session(date)}/{invoice} PART {p}", part));
            }

            _amounts.Add(total);
            var exp = Exp(true, "Inflow", total, date, customer, invoice, $"Invoice {invoice} (paid in {parts} parts)");
            Key.Add(new($"Split{parts}", false, "Proposed", [exp], lines));
        }

        public void BankChargeDeducted(int i)
        {
            var customer = Customers[(i + 2) % Customers.Length];
            var date = Day();
            var invoiced = UniqueAmount(200_000, 900_000);
            var charge = new[] { 52.50m, 26.88m, 107.50m }[i % 3];
            var invoice = $"INV-{++_invoice}";
            _amounts.Add(invoiced - charge);
            var line = Line(date, $"NIP/FRM {customer}/{Session(date)}/{invoice}", invoiced - charge);
            var exp = Exp(true, "Inflow", invoiced, date, customer, invoice, $"Invoice {invoice}");
            Key.Add(new("BankChargeDeducted", false, "Proposed", [exp], [line]));
        }

        public void UnmatchedLine(int i)
        {
            var date = Day();
            var line = i % 2 == 0
                ? Line(date, $"POS/WEB PURCHASE SHOPRITE {random.Next(10, 99)}", -UniqueAmount(2_000, 80_000))
                : Line(date, "SMS ALERT CHARGES", -UniqueAmount(4, 60));
            Key.Add(new("UnmatchedLine", false, "Unmatched", [], [line]));
        }

        public void UnmatchedExpected(int i)
        {
            var exp = Exp(i % 2 == 0, "Inflow", UniqueAmount(30_000, 900_000), Day(), Customers[(i + 1) % Customers.Length], $"INV-{++_invoice}", "Not yet paid");
            Key.Add(new("UnmatchedExpected", false, "Unmatched", [exp], []));
        }

        public List<Txn> StatementLines()
        {
            var balance = 20_000_000.00m;
            return _lines
                .OrderBy(l => l.Date).ThenBy(l => l.Reference, StringComparer.Ordinal)
                .Select(l =>
                {
                    balance += l.Amount;
                    return new Txn(l.Date, l.Date, l.Narration, l.Reference, l.Amount, balance);
                })
                .ToList();
        }

        public string ExpectedCsv()
        {
            var sb = new StringBuilder("externalId,accountNumber,direction,amount,currency,expectedDate,counterparty,reference,description\n");
            foreach (var e in _expected)
            {
                sb.Append(CultureInfo.InvariantCulture,
                    $"{e.ExternalId},{(e.AssignAccount ? AccountNumber : string.Empty)},{e.Direction},{e.Amount.ToString("0.00", CultureInfo.InvariantCulture)},NGN,{e.Date:yyyy-MM-dd},{LayoutA.Quote(e.Counterparty ?? string.Empty)},{e.Reference},{LayoutA.Quote(e.Description)}\n");
            }

            return sb.ToString();
        }

        private string Line(DateOnly date, string narration, decimal amount)
        {
            var reference = $"FT2602{++_ref:D6}";
            _lines.Add((date, narration, reference, amount));
            return reference;
        }

        private string Exp(bool assignAccount, string direction, decimal amount, DateOnly date, string? counterparty, string? reference, string description)
        {
            var id = $"EXP-{++_ext:D4}";
            _expected.Add(new Expected(id, assignAccount, direction, amount, date, counterparty, reference, description));
            return id;
        }

        /// <summary>Business day in February from the 2nd, up to <paramref name="maxOffset"/> calendar days later.</summary>
        private DateOnly Day(int maxOffset = 25)
        {
            var date = First.AddDays(random.Next(0, maxOffset));
            while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                date = date.AddDays(1);
            }

            return date;
        }

        /// <summary>
        /// An amount at least 1% away from every amount used so far, so scenarios never collide by accident
        /// (e.g. an unpaid invoice within the 0.5% tolerance of a bank-charge line would make that line ambiguous).
        /// </summary>
        private decimal UniqueAmount(int min, int max)
        {
            while (true)
            {
                var amount = Math.Round(random.Next(min, max) + (random.Next(1, 100) / 100m), 2);
                if (_amounts.All(a => Math.Abs(a - amount) > 0.01m * Math.Max(a, amount)))
                {
                    _amounts.Add(amount);
                    return amount;
                }
            }
        }

        /// <summary>How the customer appears in the ERP: sometimes "Limited" for "LTD", sometimes title case.</summary>
        private string Variant(string name) => random.Next(3) switch
        {
            0 => name.Replace(" LTD", " Limited", StringComparison.Ordinal),
            1 => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.ToLowerInvariant()),
            _ => name,
        };

        private string Session(DateOnly day) =>
            $"{random.Next(0, 1_000_000):D6}{day:yyMMdd}{random.Next(8, 18):D2}{random.Next(0, 60):D2}{random.Next(0, 60):D2}{random.Next(0, 1_000_000):D6}{random.Next(0, 1_000_000):D6}";
    }
}
