using System.Globalization;
using TreasuryKit.BuildingBlocks.Domain;
using TreasuryKit.Core.Application.Statements.Parsing;
using TreasuryKit.Core.Domain.MappingProfiles;

namespace TreasuryKit.Core.Infrastructure.Parsing;

/// <summary>Turns rows of cells into a <see cref="ParsedStatement"/> following a <see cref="BankMappingProfile"/>.</summary>
internal static class TabularStatementMapper
{
    public static Result<ParsedStatement> Map(IReadOnlyList<object?[]> rows, BankMappingProfile profile)
    {
        var culture = CultureInfo.GetCultureInfo(profile.Culture);
        if (rows.Count <= profile.HeaderRowIndex)
        {
            return ParseErrors.Invalid($"Expected a header row at row {profile.HeaderRowIndex + 1}, but the file has {rows.Count} rows.");
        }

        var header = rows[profile.HeaderRowIndex];
        var columns = ResolveColumns(header, profile);
        if (columns.IsFailure)
        {
            return columns.Error;
        }

        var map = columns.Value;
        var lines = new List<ParsedLine>();
        var firstDataRow = profile.HeaderRowIndex + 1 + profile.SkipRows;
        var tableEnd = rows.Count;

        for (var r = firstDataRow; r < rows.Count; r++)
        {
            var row = rows[r];
            var rowNumber = r + 1;
            if (row.All(c => c is null))
            {
                continue;
            }

            var dateCell = Cell(row, map.BookingDate);
            if (dateCell is null)
            {
                tableEnd = r; // footer (totals, closing balance) starts here
                break;
            }

            if (IsRepeatedHeader(row, header))
            {
                continue; // page break in a multi-page export
            }

            // A label in the date column with no amounts ("Closing Balance", "Total") ends the table.
            if (!TryDate(dateCell, profile.DateFormats, culture, out _) && HasNoAmounts(row, map))
            {
                tableEnd = r;
                break;
            }

            var line = MapLine(row, rowNumber, map, profile, culture);
            if (line.IsFailure)
            {
                return line.Error;
            }

            lines.Add(line.Value);
        }

        lines = Chronological(lines);

        var opening = Locate(rows, profile.OpeningBalanceLocator, profile.HeaderRowIndex, firstDataRow, tableEnd, culture);
        var closing = Locate(rows, profile.ClosingBalanceLocator, profile.HeaderRowIndex, firstDataRow, tableEnd, culture);

        // Derive missing balances from the running-balance column.
        if (lines.Count > 0)
        {
            opening ??= lines[0].RunningBalance - lines[0].Amount;
            closing ??= lines[^1].RunningBalance;
        }

        return new ParsedStatement(
            AccountIdentifier: null,
            Currency: null,
            PeriodStart: lines.Count > 0 ? lines.Min(l => l.BookingDate) : null,
            PeriodEnd: lines.Count > 0 ? lines.Max(l => l.BookingDate) : null,
            opening,
            closing,
            lines);
    }

    private static Result<ParsedLine> MapLine(object?[] row, int rowNumber, ColumnIndexes map, BankMappingProfile profile, CultureInfo culture)
    {
        if (!TryDate(Cell(row, map.BookingDate), profile.DateFormats, culture, out var bookingDate))
        {
            return ParseErrors.AtRow(rowNumber, $"cannot read date '{Cell(row, map.BookingDate)}' (expected {string.Join(" or ", profile.DateFormats)}).");
        }

        var valueDate = bookingDate;
        if (map.ValueDate is not null && Cell(row, map.ValueDate) is { } valueCell
            && !TryDate(valueCell, profile.DateFormats, culture, out valueDate))
        {
            return ParseErrors.AtRow(rowNumber, $"cannot read value date '{valueCell}'.");
        }

        decimal amount;
        if (profile.AmountMode == AmountMode.Signed)
        {
            if (!TryAmount(Cell(row, map.Amount), culture, out var signed) || signed is null)
            {
                return ParseErrors.AtRow(rowNumber, $"cannot read amount '{Cell(row, map.Amount)}'.");
            }

            amount = signed.Value;
        }
        else
        {
            if (!TryAmount(Cell(row, map.Debit), culture, out var debit))
            {
                return ParseErrors.AtRow(rowNumber, $"cannot read debit '{Cell(row, map.Debit)}'.");
            }

            if (!TryAmount(Cell(row, map.Credit), culture, out var credit))
            {
                return ParseErrors.AtRow(rowNumber, $"cannot read credit '{Cell(row, map.Credit)}'.");
            }

            var hasDebit = debit is not null and not 0m;
            var hasCredit = credit is not null and not 0m;
            if (hasDebit && hasCredit)
            {
                return ParseErrors.AtRow(rowNumber, "has both a debit and a credit amount.");
            }

            if (debit is null && credit is null)
            {
                return ParseErrors.AtRow(rowNumber, "has neither a debit nor a credit amount.");
            }

            amount = hasDebit ? -Math.Abs(debit!.Value) : Math.Abs(credit ?? 0m);
        }

        decimal? balance = null;
        if (map.Balance is not null)
        {
            if (!TryAmount(Cell(row, map.Balance), culture, out balance))
            {
                return ParseErrors.AtRow(rowNumber, $"cannot read balance '{Cell(row, map.Balance)}'.");
            }
        }

        var narration = Text(Cell(row, map.Narration)) ?? string.Empty;
        if (narration.Length > 1000)
        {
            narration = narration[..1000];
        }

        var reference = Text(Cell(row, map.Reference));
        if (reference is { Length: > 100 })
        {
            return ParseErrors.AtRow(rowNumber, "reference is longer than 100 characters.");
        }

        return new ParsedLine(rowNumber, bookingDate, valueDate, amount, narration, reference, balance, Text(Cell(row, map.Currency)));
    }

    /// <summary>Banks export oldest-first or newest-first; we always store oldest-first.</summary>
    private static List<ParsedLine> Chronological(List<ParsedLine> lines)
    {
        if (lines.Count < 2)
        {
            return lines;
        }

        if (lines[0].BookingDate != lines[^1].BookingDate)
        {
            return lines[0].BookingDate > lines[^1].BookingDate ? Enumerable.Reverse(lines).ToList() : lines;
        }

        // Same first and last date: let the running-balance chain decide.
        var reversed = Enumerable.Reverse(lines).ToList();
        return !ChainHolds(lines) && ChainHolds(reversed) ? reversed : lines;
    }

    private static bool ChainHolds(List<ParsedLine> lines)
    {
        for (var i = 1; i < lines.Count; i++)
        {
            if (lines[i - 1].RunningBalance is not { } previous || lines[i].RunningBalance is not { } current
                || previous + lines[i].Amount != current)
            {
                return false;
            }
        }

        return true;
    }

    private static decimal? Locate(
        IReadOnlyList<object?[]> rows,
        BalanceLocator? locator,
        int headerRowIndex,
        int firstDataRow,
        int tableEnd,
        CultureInfo culture)
    {
        if (locator is null || string.IsNullOrWhiteSpace(locator.Label))
        {
            return null;
        }

        for (var r = 0; r < rows.Count; r++)
        {
            if (r >= headerRowIndex && r < tableEnd && !(r > headerRowIndex && r < firstDataRow))
            {
                continue; // inside the header + transactions (skipped rows may hold a "B/F" balance)
            }

            var row = rows[r];
            for (var c = 0; c < row.Length; c++)
            {
                if (Text(row[c]) is { } text && text.Contains(locator.Label, StringComparison.OrdinalIgnoreCase))
                {
                    for (var v = c + 1; v < row.Length; v++)
                    {
                        if (row[v] is not null && TryAmount(row[v], culture, out var value) && value is not null)
                        {
                            return value;
                        }
                    }
                }
            }
        }

        return null;
    }

    private static Result<ColumnIndexes> ResolveColumns(object?[] header, BankMappingProfile profile)
    {
        var names = header.Select(h => NormalizeHeader(Text(h))).ToArray();
        var missing = new List<string>();

        int? Find(string field, IReadOnlyList<string>? aliases, bool required)
        {
            if (aliases is null || aliases.Count == 0)
            {
                if (required)
                {
                    missing.Add(field);
                }

                return null;
            }

            foreach (var alias in aliases)
            {
                var index = Array.IndexOf(names, NormalizeHeader(alias));
                if (index >= 0)
                {
                    return index;
                }
            }

            if (required)
            {
                missing.Add($"{field} (looked for: {string.Join(", ", aliases)})");
            }

            return null;
        }

        var m = profile.ColumnMap;
        var signed = profile.AmountMode == AmountMode.Signed;
        var result = new ColumnIndexes(
            Find("bookingDate", m.BookingDate, required: true) ?? -1,
            Find("valueDate", m.ValueDate, required: false),
            Find("narration", m.Narration, required: true) ?? -1,
            Find("reference", m.Reference, required: false),
            Find("amount", m.Amount, required: signed),
            Find("debit", m.Debit, required: !signed),
            Find("credit", m.Credit, required: !signed),
            Find("balance", m.Balance, required: false),
            Find("currency", m.Currency, required: false));

        return missing.Count == 0
            ? result
            : ParseErrors.Invalid(
                $"Header row {profile.HeaderRowIndex + 1} is missing columns: {string.Join("; ", missing)}. " +
                $"Found: {string.Join(", ", names.Where(n => n.Length > 0))}.");
    }

    private static bool HasNoAmounts(object?[] row, ColumnIndexes map) =>
        Cell(row, map.Amount) is null && Cell(row, map.Debit) is null && Cell(row, map.Credit) is null;

    private static bool IsRepeatedHeader(object?[] row, object?[] header) =>
        row.Length == header.Length
        && row.Zip(header).All(p => NormalizeHeader(Text(p.First)) == NormalizeHeader(Text(p.Second)));

    private static bool TryDate(object? cell, IReadOnlyList<string> formats, CultureInfo culture, out DateOnly date)
    {
        switch (cell)
        {
            case DateTime dateTime:
                date = DateOnly.FromDateTime(dateTime);
                return true;
            case string text:
                return DateOnly.TryParseExact(text.Trim(), formats.ToArray(), culture, DateTimeStyles.AllowWhiteSpaces, out date)
                    || (DateTime.TryParseExact(text.Trim(), formats.ToArray(), culture, DateTimeStyles.AllowWhiteSpaces, out var withTime)
                        && (date = DateOnly.FromDateTime(withTime)) == date);
            default:
                date = default;
                return false;
        }
    }

    /// <summary>Parses "1,234.56", "-1,234.56", "(1,234.56)", "1,234.56 DR/CR", "₦1,234.56". Blank → null.</summary>
    internal static bool TryAmount(object? cell, CultureInfo culture, out decimal? amount)
    {
        amount = null;
        switch (cell)
        {
            case null:
                return true;
            case decimal number:
                amount = number;
                return true;
            case string raw:
                var text = raw.Replace(" ", string.Empty, StringComparison.Ordinal)
                    .Replace(" ", string.Empty, StringComparison.Ordinal)
                    .Replace("₦", string.Empty, StringComparison.Ordinal)
                    .Replace("NGN", string.Empty, StringComparison.OrdinalIgnoreCase);
                if (text.Length == 0 || text == "-")
                {
                    return true;
                }

                var negative = false;
                if (text.StartsWith('(') && text.EndsWith(')'))
                {
                    negative = true;
                    text = text[1..^1];
                }

                if (text.EndsWith("DR", StringComparison.OrdinalIgnoreCase))
                {
                    negative = true;
                    text = text[..^2];
                }
                else if (text.EndsWith("CR", StringComparison.OrdinalIgnoreCase))
                {
                    text = text[..^2];
                }

                if (!decimal.TryParse(text, NumberStyles.Number, culture, out var parsed))
                {
                    return false;
                }

                amount = negative ? -Math.Abs(parsed) : parsed;
                return true;
            default:
                return false;
        }
    }

    private static object? Cell(object?[] row, int? index) =>
        index is { } i && i >= 0 && i < row.Length ? row[i] : null;

    private static string? Text(object? cell) => cell switch
    {
        null => null,
        string s => string.IsNullOrWhiteSpace(s) ? null : s.Trim(),
        DateTime d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => cell.ToString(),
    };

    private static string NormalizeHeader(string? text) =>
        string.Join(' ', (text ?? string.Empty).Trim('﻿', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    private sealed record ColumnIndexes(
        int BookingDate,
        int? ValueDate,
        int Narration,
        int? Reference,
        int? Amount,
        int? Debit,
        int? Credit,
        int? Balance,
        int? Currency);
}
