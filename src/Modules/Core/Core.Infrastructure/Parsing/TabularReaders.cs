using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;

namespace TreasuryKit.Core.Infrastructure.Parsing;

/// <summary>
/// Reads a tabular file into rows of typed cells: <see cref="string"/>, <see cref="decimal"/>, <see cref="DateTime"/> or null.
/// </summary>
internal static class TabularReaders
{
    public const int MaxRows = 200_000;

    public static IReadOnlyList<object?[]> ReadCsv(Stream stream, string delimiter)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = delimiter,
            HasHeaderRecord = false,
            DetectDelimiter = false,
            BadDataFound = null,
            MissingFieldFound = null,
            TrimOptions = TrimOptions.Trim,
        };

        using var parser = new CsvParser(reader, config);
        var rows = new List<object?[]>();
        while (parser.Read())
        {
            if (rows.Count >= MaxRows)
            {
                throw new InvalidDataException($"File has more than {MaxRows:N0} rows.");
            }

            rows.Add(parser.Record?.Select(v => string.IsNullOrWhiteSpace(v) ? null : (object)v).ToArray() ?? []);
        }

        return rows;
    }

    public static IReadOnlyList<object?[]> ReadXlsx(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheets.First();
        var used = sheet.RangeUsed();
        if (used is null)
        {
            return [];
        }

        var lastRow = used.LastRow().RowNumber();
        var lastColumn = used.LastColumn().ColumnNumber();
        if (lastRow > MaxRows)
        {
            throw new InvalidDataException($"File has more than {MaxRows:N0} rows.");
        }

        // Rows and columns are read from A1 so row indexes match what users see in Excel.
        var rows = new List<object?[]>(lastRow);
        for (var r = 1; r <= lastRow; r++)
        {
            var cells = new object?[lastColumn];
            for (var c = 1; c <= lastColumn; c++)
            {
                cells[c - 1] = ToCell(sheet.Cell(r, c).Value);
            }

            rows.Add(cells);
        }

        return rows;
    }

    private static object? ToCell(XLCellValue value) => value.Type switch
    {
        XLDataType.Blank => null,
        XLDataType.Number => Math.Round((decimal)value.GetNumber(), 4),
        XLDataType.DateTime => ToDateOrNumber(value),
        XLDataType.Text => string.IsNullOrWhiteSpace(value.GetText()) ? null : value.GetText().Trim(),
        XLDataType.Boolean => value.GetBoolean().ToString(CultureInfo.InvariantCulture),
        _ => value.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>A number formatted as a date outside the valid range (e.g. a balance in a date-styled column) stays a number.</summary>
    private static object ToDateOrNumber(XLCellValue value)
    {
        var serial = value.GetUnifiedNumber();
        return serial is >= 1 and < 2958466 ? DateTime.FromOADate(serial) : Math.Round((decimal)serial, 4);
    }
}
