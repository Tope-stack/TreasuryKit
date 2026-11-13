using TreasuryKit.BuildingBlocks.Domain;
using TreasuryKit.Core.Domain.MappingProfiles;
using TreasuryKit.Core.Domain.Statements;

namespace TreasuryKit.Core.Application.Statements;

public static class StatementFormats
{
    public const int SniffLength = 4096;

    /// <summary>Extensions accepted on upload; the content decides the actual format.</summary>
    public static readonly IReadOnlySet<string> AllowedExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".csv", ".xlsx", ".sta", ".mt940", ".940", ".txt", ".xml" };

    public static Error Unsupported(string fileName) =>
        Error.Validation(
            "Statement.UnsupportedFormat",
            $"Unsupported statement file '{fileName}'. Upload CSV, XLSX, MT940 (.sta/.mt940/.940/.txt) or CAMT.053 (.xml).");

    public static bool IsTabular(this StatementFormat format) => format is StatementFormat.Csv or StatementFormat.Xlsx;

    public static bool Matches(this TabularFileType fileType, StatementFormat format) =>
        (fileType, format) is (TabularFileType.Csv, StatementFormat.Csv) or (TabularFileType.Xlsx, StatementFormat.Xlsx);
}
