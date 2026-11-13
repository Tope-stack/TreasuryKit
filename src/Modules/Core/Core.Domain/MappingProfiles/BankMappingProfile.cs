using System.Globalization;
using TreasuryKit.BuildingBlocks.Domain;

namespace TreasuryKit.Core.Domain.MappingProfiles;

public enum TabularFileType
{
    Csv,
    Xlsx,
}

public enum AmountMode
{
    /// <summary>One signed amount column: credits positive, debits negative.</summary>
    Signed,

    /// <summary>Separate debit and credit columns, both positive.</summary>
    DebitCreditColumns,
}

/// <summary>
/// Header names to look for, per field. Each field lists accepted alternatives, matched case-insensitively
/// after trimming. Null means the column is absent.
/// </summary>
public sealed record ColumnMap(
    IReadOnlyList<string> BookingDate,
    IReadOnlyList<string>? ValueDate,
    IReadOnlyList<string> Narration,
    IReadOnlyList<string>? Reference,
    IReadOnlyList<string>? Amount,
    IReadOnlyList<string>? Debit,
    IReadOnlyList<string>? Credit,
    IReadOnlyList<string>? Balance,
    IReadOnlyList<string>? Currency);

/// <summary>Finds a balance outside the transaction table: a cell containing <see cref="Label"/>, value = first number to its right.</summary>
public sealed record BalanceLocator(string Label);

/// <summary>Data-driven description of one bank's CSV/XLSX export layout.</summary>
public sealed class BankMappingProfile : AggregateRoot<Guid>
{
    private BankMappingProfile(Guid id, Guid? tenantId)
        : base(id) => TenantId = tenantId;

    private BankMappingProfile()
    {
    }

    /// <summary>Null for system-wide profiles, which every tenant can use but not change (ADR 0004).</summary>
    public Guid? TenantId { get; private set; }

    public string BankCode { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public TabularFileType FileType { get; private set; }

    /// <summary>Zero-based row index of the header row.</summary>
    public int HeaderRowIndex { get; private set; }

    /// <summary>Rows to skip between the header and the first transaction (e.g. a "Balance B/F" row).</summary>
    public int SkipRows { get; private set; }

    /// <summary>CSV only.</summary>
    public string? Delimiter { get; private set; }

    /// <summary>Accepted date formats, tried in order, e.g. "dd/MM/yyyy". Typed Excel dates need none.</summary>
    public List<string> DateFormats { get; private set; } = [];

    /// <summary>Culture for numbers and month names, e.g. "en-NG".</summary>
    public string Culture { get; private set; } = null!;

    public ColumnMap ColumnMap { get; private set; } = null!;

    public AmountMode AmountMode { get; private set; }

    public BalanceLocator? OpeningBalanceLocator { get; private set; }

    public BalanceLocator? ClosingBalanceLocator { get; private set; }

    public bool IsSystem => TenantId is null;

    public static Result<BankMappingProfile> Create(
        Guid? tenantId,
        string? bankCode,
        string? name,
        TabularFileType fileType,
        int headerRowIndex,
        int skipRows,
        string? delimiter,
        IReadOnlyList<string>? dateFormats,
        string? culture,
        ColumnMap? columnMap,
        AmountMode amountMode,
        BalanceLocator? openingBalanceLocator,
        BalanceLocator? closingBalanceLocator)
    {
        bankCode = bankCode?.Trim().ToUpperInvariant();
        name = name?.Trim();
        culture = string.IsNullOrWhiteSpace(culture) ? "en-NG" : culture.Trim();
        var formats = (dateFormats ?? []).Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f.Trim()).ToList();

        var problems = new List<string>();
        if (string.IsNullOrEmpty(bankCode) || bankCode.Length > 20)
        {
            problems.Add("bankCode is required (max 20 characters)");
        }

        if (string.IsNullOrEmpty(name) || name.Length > 200)
        {
            problems.Add("name is required (max 200 characters)");
        }

        if (headerRowIndex is < 0 or > 100 || skipRows is < 0 or > 100)
        {
            problems.Add("headerRowIndex and skipRows must be between 0 and 100");
        }

        if (fileType == TabularFileType.Csv && string.IsNullOrEmpty(delimiter))
        {
            problems.Add("delimiter is required for CSV profiles");
        }

        if (formats.Count == 0)
        {
            problems.Add("at least one date format is required");
        }

        if (!IsKnownCulture(culture))
        {
            problems.Add($"culture '{culture}' is not recognised");
        }

        if (columnMap is null)
        {
            problems.Add("columnMap is required");
        }
        else
        {
            if (IsEmpty(columnMap.BookingDate))
            {
                problems.Add("columnMap.bookingDate is required");
            }

            if (IsEmpty(columnMap.Narration))
            {
                problems.Add("columnMap.narration is required");
            }

            if (amountMode == AmountMode.Signed && IsEmpty(columnMap.Amount))
            {
                problems.Add("columnMap.amount is required when amountMode is Signed");
            }

            if (amountMode == AmountMode.DebitCreditColumns && (IsEmpty(columnMap.Debit) || IsEmpty(columnMap.Credit)))
            {
                problems.Add("columnMap.debit and columnMap.credit are required when amountMode is DebitCreditColumns");
            }
        }

        if (problems.Count > 0)
        {
            return Error.Validation("MappingProfile.Invalid", string.Join("; ", problems) + ".");
        }

        return new BankMappingProfile(Guid.CreateVersion7(), tenantId)
        {
            BankCode = bankCode!,
            Name = name!,
            FileType = fileType,
            HeaderRowIndex = headerRowIndex,
            SkipRows = skipRows,
            Delimiter = fileType == TabularFileType.Csv ? delimiter : null,
            DateFormats = formats,
            Culture = culture,
            ColumnMap = columnMap!,
            AmountMode = amountMode,
            OpeningBalanceLocator = openingBalanceLocator,
            ClosingBalanceLocator = closingBalanceLocator,
        };
    }

    private static bool IsEmpty(IReadOnlyList<string>? names) => names is null || !names.Any(n => !string.IsNullOrWhiteSpace(n));

    private static bool IsKnownCulture(string culture)
    {
        try
        {
            _ = CultureInfo.GetCultureInfo(culture, predefinedOnly: true);
            return true;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }
}

public static class MappingProfileErrors
{
    public static readonly Error NotFound = Error.NotFound("MappingProfile.NotFound", "Mapping profile not found.");
}
