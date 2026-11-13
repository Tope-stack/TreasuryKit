using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TreasuryKit.BuildingBlocks.Domain;
using TreasuryKit.Core.Application.Statements.Parsing;
using TreasuryKit.Core.Domain.Statements;

namespace TreasuryKit.Core.Infrastructure.Parsing;

/// <summary>
/// SWIFT MT940 customer statements: with or without the {1:}{2:}{4:…-} envelope, one or more statements per file.
/// Tags: :20: :25: :28C: :60F/M: :61: :86: :62F/M:.
/// </summary>
internal sealed partial class Mt940StatementParser : IStatementParser
{
    /// <summary>MT940 :86: lines are at most 65 characters; a full line was wrapped mid-word.</summary>
    private const int LineWidth = 65;

    public StatementFormat Format => StatementFormat.Mt940;

    public bool CanParse(ReadOnlySpan<byte> header, string fileName)
    {
        var text = Encoding.UTF8.GetString(header).TrimStart('\uFEFF', ' ', '\r', '\n', '\t');
        return text.StartsWith("{1:", StringComparison.Ordinal) || text.StartsWith(":20:", StringComparison.Ordinal);
    }

    public async Task<Result<ParsedStatement>> ParseAsync(Stream stream, ParseContext context, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var text = await reader.ReadToEndAsync(cancellationToken);
        try
        {
            return Parse(text);
        }
        catch (Exception ex) when (ex is InvalidDataException or FormatException or ArgumentOutOfRangeException)
        {
            return ParseErrors.Invalid($"Cannot read MT940 file: {ex.Message}");
        }
    }

    private static Result<ParsedStatement> Parse(string text)
    {
        var statements = new List<Statement>();
        Statement? current = null;
        Transaction? lastTransaction = null;

        foreach (var (tag, value) in Fields(text))
        {
            switch (tag)
            {
                case "20":
                    current = new Statement();
                    statements.Add(current);
                    lastTransaction = null;
                    break;
                case "25":
                    Require(current).Account = value.Trim();
                    break;
                case "60F" or "60M":
                    var opening = ParseBalance(value);
                    if (opening.IsFailure)
                    {
                        return opening.Error;
                    }

                    Require(current).Opening = opening.Value;
                    break;
                case "61":
                    var transaction = ParseTransaction(value, statements.Sum(s => s.Transactions.Count) + 1);
                    if (transaction.IsFailure)
                    {
                        return transaction.Error;
                    }

                    lastTransaction = transaction.Value;
                    Require(current).Transactions.Add(lastTransaction);
                    break;
                case "86" when lastTransaction is not null:
                    lastTransaction.Information = Unwrap(value);
                    lastTransaction = null; // a statement-level :86: after this belongs to nobody
                    break;
                case "62F" or "62M":
                    var closing = ParseBalance(value);
                    if (closing.IsFailure)
                    {
                        return closing.Error;
                    }

                    Require(current).Closing = closing.Value;
                    lastTransaction = null;
                    break;
            }
        }

        return Combine(statements);
    }

    private static Result<ParsedStatement> Combine(List<Statement> statements)
    {
        if (statements.Count == 0)
        {
            return ParseErrors.Invalid("No MT940 statement found (expected a :20: tag).");
        }

        var accounts = statements.Select(s => s.Account).Where(a => a is not null).Distinct().ToList();
        if (accounts.Count != 1)
        {
            return ParseErrors.Invalid(accounts.Count == 0
                ? "The MT940 file has no :25: account."
                : $"The MT940 file contains statements for several accounts ({string.Join(", ", accounts)}); upload one account per file.");
        }

        if (statements.Any(s => s.Opening is null || s.Closing is null))
        {
            return ParseErrors.Invalid("Every MT940 statement needs an opening (:60F:/:60M:) and closing (:62F:/:62M:) balance.");
        }

        var currencies = statements.Select(s => s.Opening!.Currency).Concat(statements.Select(s => s.Closing!.Currency)).Distinct().ToList();
        if (currencies.Count != 1)
        {
            return ParseErrors.Invalid($"The MT940 file mixes currencies ({string.Join(", ", currencies)}).");
        }

        var currency = Currency.FromCode(currencies[0]);
        if (currency.IsFailure)
        {
            return currency.Error;
        }

        // Each statement must balance and follow on from the previous one.
        string? issue = null;
        for (var i = 0; i < statements.Count && issue is null; i++)
        {
            var s = statements[i];
            var expected = s.Opening!.Amount + s.Transactions.Sum(t => t.Amount);
            if (expected != s.Closing!.Amount)
            {
                issue = $"Statement {i + 1}: opening {s.Opening.Amount:N2} + lines {expected - s.Opening.Amount:N2} ≠ closing {s.Closing.Amount:N2}.";
            }
            else if (i > 0 && s.Opening.Amount != statements[i - 1].Closing!.Amount)
            {
                issue = $"Statement {i + 1} opens at {s.Opening.Amount:N2} but statement {i} closed at {statements[i - 1].Closing!.Amount:N2}.";
            }
        }

        var lines = statements.SelectMany(s => s.Transactions).Select(t => t.ToParsedLine()).ToList();
        return new ParsedStatement(
            accounts[0],
            currency.Value,
            lines.Count > 0 ? lines.Min(l => l.BookingDate) : statements[0].Opening!.Date,
            lines.Count > 0 ? lines.Max(l => l.BookingDate) : statements[^1].Closing!.Date,
            statements[0].Opening!.Amount,
            statements[^1].Closing!.Amount,
            lines,
            issue);
    }

    /// <summary>Splits the text into (tag, value) fields; continuation lines are kept with their line breaks.</summary>
    private static IEnumerable<(string Tag, string Value)> Fields(string text)
    {
        string? tag = null;
        var value = new StringBuilder();
        foreach (var rawLine in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var envelopeBody = line.IndexOf("{4:", StringComparison.Ordinal);
            if (envelopeBody >= 0)
            {
                line = line[(envelopeBody + 3)..];
            }

            if (line.StartsWith('-') && (line.Length == 1 || line[1] == '}'))
            {
                continue; // end of message
            }

            var match = TagLine().Match(line);
            if (match.Success)
            {
                if (tag is not null)
                {
                    yield return (tag, value.ToString());
                }

                tag = match.Groups["tag"].Value;
                value.Clear().Append(match.Groups["value"].Value);
            }
            else if (tag is not null && line.Length > 0 && !line.StartsWith('{'))
            {
                value.Append('\n').Append(line);
            }
        }

        if (tag is not null)
        {
            yield return (tag, value.ToString());
        }
    }

    private static Result<Balance> ParseBalance(string value)
    {
        var match = BalanceField().Match(value.Trim());
        if (!match.Success)
        {
            return ParseErrors.Invalid($"Cannot read MT940 balance '{value.Trim()}'.");
        }

        var amount = Amount(match.Groups["amount"].Value);
        return new Balance(
            match.Groups["mark"].Value == "D" ? -amount : amount,
            match.Groups["currency"].Value,
            Date(match.Groups["date"].Value));
    }

    private static Result<Transaction> ParseTransaction(string value, int ordinal)
    {
        var lines = value.Split('\n');
        var match = TransactionField().Match(lines[0].Trim());
        if (!match.Success)
        {
            return ParseErrors.Invalid($"Transaction {ordinal}: cannot read :61: '{lines[0].Trim()}'.");
        }

        var valueDate = Date(match.Groups["valueDate"].Value);
        var bookingDate = valueDate;
        if (match.Groups["entryDate"].Success)
        {
            var month = int.Parse(match.Groups["entryDate"].Value[..2], CultureInfo.InvariantCulture);
            var day = int.Parse(match.Groups["entryDate"].Value[2..], CultureInfo.InvariantCulture);
            var year = valueDate.Year + (month, valueDate.Month) switch
            {
                (12, 1) => -1, // booked in December for a January value date
                (1, 12) => 1,
                _ => 0,
            };
            bookingDate = new DateOnly(year, month, day);
        }

        // C/RD increase the balance; D/RC (and reversal of credit) decrease it.
        var amount = Amount(match.Groups["amount"].Value);
        var signed = match.Groups["mark"].Value is "D" or "RC" ? -amount : amount;

        var customerRef = match.Groups["customerRef"].Value.Trim();
        var bankRef = match.Groups["bankRef"].Value.Trim();
        var reference = new[] { bankRef, customerRef }
            .FirstOrDefault(r => r.Length > 0 && !string.Equals(r, "NONREF", StringComparison.OrdinalIgnoreCase));

        return new Transaction(
            ordinal,
            bookingDate,
            valueDate,
            signed,
            reference is { Length: > 100 } ? reference[..100] : reference,
            string.Join(' ', lines.Skip(1).Select(l => l.Trim()).Where(l => l.Length > 0)),
            customerRef);
    }

    /// <summary>Joins :86: lines: no space after a full-width line (wrapped mid-word), a space otherwise.</summary>
    internal static string Unwrap(string value)
    {
        var lines = value.Split('\n');
        var builder = new StringBuilder(lines[0]);
        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i - 1].Length < LineWidth)
            {
                builder.Append(' ');
            }

            builder.Append(lines[i]);
        }

        return builder.ToString().Trim();
    }

    private static decimal Amount(string value) =>
        decimal.Parse(value.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);

    private static DateOnly Date(string yymmdd) =>
        DateOnly.ParseExact(yymmdd, "yyMMdd", CultureInfo.InvariantCulture);

    private static Statement Require(Statement? statement) =>
        statement ?? throw new InvalidDataException("MT940 field found before the first :20: tag.");

    [GeneratedRegex(@"^:(?<tag>\d{2}[A-Z]?):(?<value>.*)$")]
    private static partial Regex TagLine();

    [GeneratedRegex(@"^(?<mark>[CD])(?<date>\d{6})(?<currency>[A-Z]{3})(?<amount>\d+,\d*)$")]
    private static partial Regex BalanceField();

    // YYMMDD [MMDD] mark [funds code] amount type customer-ref [//bank-ref] — see SWIFT MT940 field 61.
    [GeneratedRegex(@"^(?<valueDate>\d{6})(?<entryDate>\d{4})?(?<mark>RC|RD|EC|ED|C|D)(?<funds>[A-Z])?(?<amount>\d+,\d*)(?<type>[NFS][A-Z0-9]{3})(?<customerRef>.*?)(?://(?<bankRef>.*))?$")]
    private static partial Regex TransactionField();

    private sealed record Balance(decimal Amount, string Currency, DateOnly Date);

    private sealed class Statement
    {
        public string? Account { get; set; }

        public Balance? Opening { get; set; }

        public Balance? Closing { get; set; }

        public List<Transaction> Transactions { get; } = [];
    }

    private sealed class Transaction(
        int ordinal,
        DateOnly bookingDate,
        DateOnly valueDate,
        decimal amount,
        string? reference,
        string supplementary,
        string customerRef)
    {
        public decimal Amount => amount;

        public string? Information { get; set; }

        public ParsedLine ToParsedLine()
        {
            var narration = new[] { Information, supplementary, customerRef }
                .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n) && !string.Equals(n, "NONREF", StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
            return new ParsedLine(ordinal, bookingDate, valueDate, amount, narration.Length > 1000 ? narration[..1000] : narration, reference, null, null);
        }
    }
}
