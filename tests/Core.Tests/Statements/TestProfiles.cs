using System.Text.Json;
using TreasuryKit.Core.Domain.MappingProfiles;

namespace TreasuryKit.Core.Tests.Statements;

/// <summary>The generic layouts, defined exactly as the seeded system profiles in the migration.</summary>
internal static class TestProfiles
{
    public static readonly BankMappingProfile LayoutA = BankMappingProfile.Create(
        null, "GENERIC", "Layout A", TabularFileType.Csv, 0, 0, ",", ["dd/MM/yyyy"], "en-NG",
        new ColumnMap(["Trans Date", "Transaction Date"], ["Value Date"], ["Description", "Narration"], ["Reference"], ["Amount"], null, null, ["Balance"], null),
        AmountMode.Signed, null, null).Value;

    public static readonly BankMappingProfile LayoutB = BankMappingProfile.Create(
        null, "GENERIC", "Layout B", TabularFileType.Xlsx, 6, 0, null, ["dd/MM/yyyy", "dd-MMM-yyyy"], "en-NG",
        new ColumnMap(["Post Date"], ["Value Date"], ["Narration"], ["Ref", "Reference"], null, ["Debit"], ["Credit"], ["Balance"], null),
        AmountMode.DebitCreditColumns, new BalanceLocator("Opening Balance"), new BalanceLocator("Closing Balance")).Value;

    public static readonly BankMappingProfile LayoutC = BankMappingProfile.Create(
        null, "GENERIC", "Layout C", TabularFileType.Csv, 0, 0, ",", ["dd-MMM-yyyy"], "en-NG",
        new ColumnMap(["Txn Date"], null, ["Remarks"], ["Ref No"], null, ["Withdrawals"], ["Deposits"], ["Running Balance"], null),
        AmountMode.DebitCreditColumns, null, null).Value;

    /// <summary>The profile for a golden file; null for self-describing formats (MT940, CAMT.053).</summary>
    public static BankMappingProfile? For(string file) => file.Split('/')[0] switch
    {
        "layout-a" => LayoutA,
        "layout-b" => LayoutB,
        "layout-c" => LayoutC,
        _ => null,
    };
}

internal sealed record ManifestEntry(
    int LineCount,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal OpeningBalance,
    decimal ClosingBalance,
    decimal SumOfAmounts,
    int NipSessionIds,
    bool Balanced,
    bool NewestFirst,
    int OverlapWithPrevious,
    string? Account,
    int SkippedEntries);

internal static class GoldenFiles
{
    public static readonly string Root = Path.Combine(AppContext.BaseDirectory, "TestData", "statements");

    public static readonly IReadOnlyDictionary<string, ManifestEntry> Manifest =
        JsonSerializer.Deserialize<Dictionary<string, ManifestEntry>>(
            File.ReadAllText(Path.Combine(Root, "manifest.json")),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    public static Stream Open(string file) => File.OpenRead(Path.Combine(Root, file));
}
