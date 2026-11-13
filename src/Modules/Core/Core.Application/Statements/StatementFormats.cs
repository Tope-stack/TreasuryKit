using TreasuryKit.BuildingBlocks.Domain;
using TreasuryKit.Core.Domain.MappingProfiles;
using TreasuryKit.Core.Domain.Statements;

namespace TreasuryKit.Core.Application.Statements;

public static class StatementFormats
{
    /// <summary>Phase 1 accepts CSV and XLSX by extension; MT940/CAMT.053 sniffing arrives in Phase 2.</summary>
    public static Result<StatementFormat> FromFileName(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".csv" => StatementFormat.Csv,
            ".xlsx" => StatementFormat.Xlsx,
            var ext => Error.Validation("Statement.UnsupportedFormat", $"Unsupported statement file type '{ext}'. Upload .csv or .xlsx."),
        };

    public static bool Matches(this TabularFileType fileType, StatementFormat format) =>
        (fileType, format) is (TabularFileType.Csv, StatementFormat.Csv) or (TabularFileType.Xlsx, StatementFormat.Xlsx);
}
