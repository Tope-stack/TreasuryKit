using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using TreasuryKit.Recon.Application.Reports;

namespace TreasuryKit.Recon.Infrastructure.Reports;

internal sealed class ReportExportWriter : IReportExportWriter
{
    private static readonly string[] Headers =
        ["Item type", "Item id", "Date", "Amount", "Currency", "Description", "Reference", "Status", "Match id", "Rule", "Confidence", "Matched with"];

    public ExportFile Write(IReadOnlyList<ExportRow> rows, ExportFormat format, string baseName) => format switch
    {
        ExportFormat.Xlsx => new ExportFile($"{baseName}.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", Xlsx(rows)),
        _ => new ExportFile($"{baseName}.csv", "text/csv", Csv(rows)),
    };

    private static byte[] Csv(IReadOnlyList<ExportRow> rows)
    {
        var sb = new StringBuilder(string.Join(',', Headers)).Append('\n');
        foreach (var r in rows)
        {
            string?[] values =
            [
                r.ItemType, r.ItemId.ToString(), r.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                r.Amount.ToString("0.00", CultureInfo.InvariantCulture), r.Currency, r.Description, r.Reference, r.Status,
                r.MatchId?.ToString(), r.RuleKey, r.Confidence?.ToString("0.####", CultureInfo.InvariantCulture), r.MatchedWith,
            ];
            sb.AppendJoin(',', values.Select(Escape)).Append('\n');
        }

        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    /// <summary>Quotes fields with separators, and neutralises leading formula characters (CSV injection).</summary>
    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value[0] is '=' or '+' or '@' or '\t' or '\r' || (value[0] == '-' && !decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _)))
        {
            value = "'" + value;
        }

        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : value;
    }

    private static byte[] Xlsx(IReadOnlyList<ExportRow> rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Reconciliation");
        for (var c = 0; c < Headers.Length; c++)
        {
            sheet.Cell(1, c + 1).Value = Headers[c];
        }

        sheet.Row(1).Style.Font.Bold = true;
        var row = 2;
        foreach (var r in rows)
        {
            sheet.Cell(row, 1).Value = r.ItemType;
            sheet.Cell(row, 2).Value = r.ItemId.ToString();
            sheet.Cell(row, 3).Value = r.Date.ToDateTime(TimeOnly.MinValue);
            sheet.Cell(row, 3).Style.DateFormat.Format = "yyyy-mm-dd";
            sheet.Cell(row, 4).Value = r.Amount;
            sheet.Cell(row, 4).Style.NumberFormat.Format = "#,##0.00";
            sheet.Cell(row, 5).Value = r.Currency;
            sheet.Cell(row, 6).Value = r.Description;
            sheet.Cell(row, 7).Value = r.Reference ?? string.Empty;
            sheet.Cell(row, 8).Value = r.Status;
            sheet.Cell(row, 9).Value = r.MatchId?.ToString() ?? string.Empty;
            sheet.Cell(row, 10).Value = r.RuleKey ?? string.Empty;
            if (r.Confidence is { } confidence)
            {
                sheet.Cell(row, 11).Value = confidence;
            }

            sheet.Cell(row, 12).Value = r.MatchedWith ?? string.Empty;
            row++;
        }

        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents(1, Math.Min(row, 200));
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
