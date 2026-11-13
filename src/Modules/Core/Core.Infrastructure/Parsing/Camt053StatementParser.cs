using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using TreasuryKit.BuildingBlocks.Domain;
using TreasuryKit.Core.Application.Statements.Parsing;
using TreasuryKit.Core.Domain.Statements;

namespace TreasuryKit.Core.Infrastructure.Parsing;

/// <summary>
/// ISO 20022 camt.053 (BankToCustomerStatement), any 001.xx version: elements are matched by local name.
/// Booked entries only; one line per entry (batch entries are not split yet).
/// </summary>
internal sealed class Camt053StatementParser : IStatementParser
{
    private const string NamespaceMarker = "camt.053";

    public StatementFormat Format => StatementFormat.Camt053;

    public bool CanParse(ReadOnlySpan<byte> header, string fileName)
    {
        var text = Encoding.UTF8.GetString(header);
        return text.TrimStart('﻿', ' ', '\r', '\n', '\t').StartsWith('<') && text.Contains(NamespaceMarker, StringComparison.Ordinal);
    }

    public async Task<Result<ParsedStatement>> ParseAsync(Stream stream, ParseContext context, CancellationToken cancellationToken)
    {
        // No DTDs, no external resolution: statement files come from users.
        var settings = new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 100_000_000,
        };

        XDocument document;
        try
        {
            using var reader = XmlReader.Create(stream, settings);
            document = await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken);
        }
        catch (XmlException ex)
        {
            return ParseErrors.Invalid($"Cannot read CAMT.053 XML: {ex.Message}");
        }

        if (document.Root is null || !document.Root.Name.NamespaceName.Contains(NamespaceMarker, StringComparison.Ordinal))
        {
            return ParseErrors.Invalid("Not a camt.053 document.");
        }

        try
        {
            return Parse(document.Root);
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException)
        {
            return ParseErrors.Invalid($"Cannot read CAMT.053: {ex.Message}");
        }
    }

    private static Result<ParsedStatement> Parse(XElement root)
    {
        var statements = root.Descendants().Where(e => e.Name.LocalName == "Stmt").ToList();
        if (statements.Count == 0)
        {
            return ParseErrors.Invalid("The CAMT.053 document contains no Stmt element.");
        }

        var lines = new List<ParsedLine>();
        var accounts = new HashSet<string>(StringComparer.Ordinal);
        var currencies = new HashSet<string>(StringComparer.Ordinal);
        decimal? firstOpening = null;
        decimal? lastClosing = null;
        string? issue = null;
        var skipped = 0;
        var ordinal = 0;

        for (var i = 0; i < statements.Count; i++)
        {
            var statement = statements[i];
            var account = statement.Path("Acct", "Id", "IBAN")?.Value ?? statement.Path("Acct", "Id", "Othr", "Id")?.Value;
            if (account is null)
            {
                return ParseErrors.Invalid($"Statement {i + 1} has no account id.");
            }

            accounts.Add(account.Trim());
            if (statement.Path("Acct", "Ccy")?.Value is { } accountCurrency)
            {
                currencies.Add(accountCurrency.Trim());
            }

            var opening = FindBalance(statement, "OPBD") ?? FindBalance(statement, "PRCD");
            var closing = FindBalance(statement, "CLBD");
            if (opening is not null)
            {
                currencies.Add(opening.Value.Currency);
            }

            var statementSum = 0m;
            foreach (var entry in statement.Children("Ntry"))
            {
                var status = entry.Path("Sts", "Cd")?.Value ?? entry.Child("Sts")?.Value;
                if (status is not null && !string.Equals(status.Trim(), "BOOK", StringComparison.OrdinalIgnoreCase))
                {
                    skipped++;
                    continue;
                }

                var line = ParseEntry(entry, ++ordinal);
                currencies.Add(line.CurrencyCode!);
                statementSum += line.Amount;
                lines.Add(line);
            }

            firstOpening ??= opening?.Amount;
            if (issue is null && opening is not null && closing is not null && opening.Value.Amount + statementSum != closing.Value.Amount)
            {
                issue = $"Statement {i + 1}: opening {opening.Value.Amount:N2} + entries {statementSum:N2} ≠ closing {closing.Value.Amount:N2}.";
            }

            if (issue is null && i > 0 && opening is not null && lastClosing is not null && opening.Value.Amount != lastClosing)
            {
                issue = $"Statement {i + 1} opens at {opening.Value.Amount:N2} but statement {i} closed at {lastClosing:N2}.";
            }

            lastClosing = closing?.Amount ?? lastClosing;
        }

        if (accounts.Count > 1)
        {
            return ParseErrors.Invalid($"The CAMT.053 file contains statements for several accounts ({string.Join(", ", accounts)}); upload one account per file.");
        }

        if (currencies.Count > 1)
        {
            return ParseErrors.Invalid($"The CAMT.053 file mixes currencies ({string.Join(", ", currencies)}).");
        }

        Currency? currency = null;
        if (currencies.Count == 1)
        {
            var parsed = Currency.FromCode(currencies.Single());
            if (parsed.IsFailure)
            {
                return parsed.Error;
            }

            currency = parsed.Value;
        }

        lines = lines.OrderBy(l => l.BookingDate).ThenBy(l => l.RowNumber).ToList();
        return new ParsedStatement(
            accounts.Single(),
            currency,
            lines.Count > 0 ? lines.Min(l => l.BookingDate) : null,
            lines.Count > 0 ? lines.Max(l => l.BookingDate) : null,
            firstOpening,
            lastClosing,
            lines,
            issue,
            skipped);
    }

    private static ParsedLine ParseEntry(XElement entry, int ordinal)
    {
        var amountElement = entry.Child("Amt") ?? throw new InvalidDataException($"Entry {ordinal} has no Amt.");
        var amount = decimal.Parse(amountElement.Value.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        var credit = entry.Child("CdtDbtInd")?.Value.Trim() switch
        {
            "CRDT" => true,
            "DBIT" => false,
            var other => throw new InvalidDataException($"Entry {ordinal} has CdtDbtInd '{other}'."),
        };

        var bookingDate = Date(entry.Child("BookgDt")) ?? Date(entry.Child("ValDt"))
            ?? throw new InvalidDataException($"Entry {ordinal} has no booking or value date.");
        var valueDate = Date(entry.Child("ValDt")) ?? bookingDate;

        var transactions = entry.Child("NtryDtls")?.Children("TxDtls").ToList() ?? [];
        var single = transactions.Count == 1 ? transactions[0] : null;

        var narrationParts = new List<string?> { entry.Child("AddtlNtryInf")?.Value };
        foreach (var tx in transactions)
        {
            narrationParts.AddRange(tx.Path("RmtInf")?.Children("Ustrd").Select(u => u.Value) ?? []);
            narrationParts.Add(tx.Child("AddtlTxInf")?.Value);
        }

        var counterpartyName = single is null
            ? null
            : credit
                ? Name(single, "Dbtr")
                : Name(single, "Cdtr");
        var narration = string.Join(' ', narrationParts
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => string.Join(' ', p!.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))
            .Distinct());
        if (narration.Length == 0)
        {
            narration = counterpartyName ?? string.Empty;
        }

        var bankReference = entry.Child("AcctSvcrRef")?.Value.Trim() ?? single?.Path("Refs", "AcctSvcrRef")?.Value.Trim();
        var endToEnd = single?.Path("Refs", "EndToEndId")?.Value.Trim();

        return new ParsedLine(
            ordinal,
            bookingDate,
            valueDate,
            credit ? amount : -amount,
            narration.Length > 1000 ? narration[..1000] : narration,
            bankReference is { Length: > 100 } ? bankReference[..100] : bankReference,
            RunningBalance: null,
            CurrencyCode: amountElement.Attribute("Ccy")?.Value,
            EndToEndId: endToEnd is { Length: > 35 } ? endToEnd[..35] : endToEnd,
            CounterpartyName: counterpartyName);
    }

    /// <summary>Party name: "Dbtr/Nm" (camt.053.001.02) or "Dbtr/Pty/Nm" (001.08 and later).</summary>
    private static string? Name(XElement transaction, string role)
    {
        var party = transaction.Path("RltdPties", role);
        var name = party?.Child("Nm")?.Value ?? party?.Path("Pty", "Nm")?.Value;
        return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
    }

    private static (decimal Amount, string Currency)? FindBalance(XElement statement, string code)
    {
        var balance = statement.Children("Bal")
            .FirstOrDefault(b => string.Equals(b.Path("Tp", "CdOrPrtry", "Cd")?.Value.Trim(), code, StringComparison.Ordinal));
        if (balance?.Child("Amt") is not { } amountElement)
        {
            return null;
        }

        var amount = decimal.Parse(amountElement.Value.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        var debit = balance.Child("CdtDbtInd")?.Value.Trim() == "DBIT";
        return (debit ? -amount : amount, amountElement.Attribute("Ccy")?.Value ?? string.Empty);
    }

    private static DateOnly? Date(XElement? container)
    {
        if (container is null)
        {
            return null;
        }

        if (container.Child("Dt")?.Value is { } date)
        {
            return DateOnly.ParseExact(date.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (container.Child("DtTm")?.Value is { } dateTime)
        {
            return DateOnly.FromDateTime(DateTimeOffset.Parse(dateTime.Trim(), CultureInfo.InvariantCulture).DateTime);
        }

        return null;
    }
}

internal static class XElementLocalNameExtensions
{
    public static XElement? Child(this XElement element, string localName) =>
        element.Elements().FirstOrDefault(e => e.Name.LocalName == localName);

    public static IEnumerable<XElement> Children(this XElement element, string localName) =>
        element.Elements().Where(e => e.Name.LocalName == localName);

    public static XElement? Path(this XElement element, params string[] localNames)
    {
        XElement? current = element;
        foreach (var name in localNames)
        {
            current = current?.Child(name);
        }

        return current;
    }
}
