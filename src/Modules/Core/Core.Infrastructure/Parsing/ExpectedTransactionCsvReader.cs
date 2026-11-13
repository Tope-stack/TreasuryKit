using TreasuryKit.BuildingBlocks.Domain;
using TreasuryKit.Core.Application.ExpectedTransactions;

namespace TreasuryKit.Core.Infrastructure.Parsing;

internal sealed class ExpectedTransactionCsvReader : IExpectedTransactionCsvReader
{
    private static readonly string[] Required = ["externalId", "direction", "amount", "expectedDate"];
    private static readonly string[] Optional = ["accountNumber", "currency", "counterparty", "reference", "description"];

    public Result<IReadOnlyList<ExpectedTransactionCsvRow>> Read(Stream stream)
    {
        IReadOnlyList<object?[]> rows;
        try
        {
            rows = TabularReaders.ReadCsv(stream, ",");
        }
        catch (Exception ex) when (ex is InvalidDataException or CsvHelper.CsvHelperException)
        {
            return Error.Validation("ExpectedTransactionImport.Invalid", $"Cannot read CSV: {ex.Message}");
        }

        if (rows.Count == 0)
        {
            return Error.Validation("ExpectedTransactionImport.Invalid", "The file has no header row.");
        }

        var header = rows[0].Select(h => (h as string)?.Trim('﻿', ' ') ?? string.Empty).ToArray();
        int Index(string name) => Array.FindIndex(header, h => string.Equals(h, name, StringComparison.OrdinalIgnoreCase));

        var missing = Required.Where(r => Index(r) < 0).ToList();
        if (missing.Count > 0)
        {
            return Error.Validation(
                "ExpectedTransactionImport.Invalid",
                $"Missing columns: {string.Join(", ", missing)}. Expected: {string.Join(", ", Required.Concat(Optional))}.");
        }

        var columns = Required.Concat(Optional).ToDictionary(n => n, Index, StringComparer.OrdinalIgnoreCase);
        string? Get(object?[] row, string name) =>
            columns[name] is var i && i >= 0 && i < row.Length ? (row[i] as string)?.Trim() : null;

        var result = new List<ExpectedTransactionCsvRow>();
        for (var r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            if (row.All(c => c is null))
            {
                continue;
            }

            result.Add(new ExpectedTransactionCsvRow(
                r + 1,
                Get(row, "externalId"),
                Get(row, "accountNumber"),
                Get(row, "direction"),
                Get(row, "amount"),
                Get(row, "currency"),
                Get(row, "expectedDate"),
                Get(row, "counterparty"),
                Get(row, "reference"),
                Get(row, "description")));
        }

        return result;
    }
}
