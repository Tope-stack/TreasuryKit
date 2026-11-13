using TreasuryKit.BuildingBlocks.Domain;

namespace TreasuryKit.Core.Domain.Accounts;

public sealed class BankAccount : AggregateRoot<Guid>, ITenantOwned
{
    private BankAccount(Guid id, Guid tenantId)
        : base(id) => TenantId = tenantId;

    private BankAccount()
    {
    }

    public Guid TenantId { get; private set; }

    /// <summary>Bank identifier, e.g. the CBN bank code ("058") or a BIC.</summary>
    public string BankCode { get; private set; } = null!;

    public string BankName { get; private set; } = null!;

    public string AccountNumber { get; private set; } = null!;

    public Currency Currency { get; private set; } = null!;

    public string DisplayName { get; private set; } = null!;

    /// <summary>How this bank's CSV/XLSX exports are read. Required to import tabular statements.</summary>
    public Guid? MappingProfileId { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Result<BankAccount> Create(
        Guid tenantId,
        string? bankCode,
        string? bankName,
        string? accountNumber,
        string? currencyCode,
        string? displayName,
        Guid? mappingProfileId,
        DateTimeOffset now)
    {
        bankCode = bankCode?.Trim().ToUpperInvariant();
        bankName = bankName?.Trim();
        accountNumber = accountNumber?.Replace(" ", string.Empty, StringComparison.Ordinal).Trim();

        if (string.IsNullOrEmpty(bankCode) || bankCode.Length > 20)
        {
            return BankAccountErrors.Invalid("bankCode", "is required (max 20 characters)");
        }

        if (string.IsNullOrEmpty(bankName) || bankName.Length > 100)
        {
            return BankAccountErrors.Invalid("bankName", "is required (max 100 characters)");
        }

        if (string.IsNullOrEmpty(accountNumber) || accountNumber.Length > 34 || !accountNumber.All(char.IsLetterOrDigit))
        {
            return BankAccountErrors.Invalid("accountNumber", "is required: up to 34 letters or digits");
        }

        var currency = Currency.FromCode(currencyCode);
        if (currency.IsFailure)
        {
            return currency.Error;
        }

        displayName = string.IsNullOrWhiteSpace(displayName) ? $"{bankName} {accountNumber} ({currency.Value.Code})" : displayName.Trim();
        if (displayName.Length > 200)
        {
            return BankAccountErrors.Invalid("displayName", "must be at most 200 characters");
        }

        return new BankAccount(Guid.CreateVersion7(), tenantId)
        {
            BankCode = bankCode,
            BankName = bankName,
            AccountNumber = accountNumber,
            Currency = currency.Value,
            DisplayName = displayName,
            MappingProfileId = mappingProfileId,
            IsActive = true,
            CreatedAt = now,
        };
    }
}

public static class BankAccountErrors
{
    public static readonly Error NotFound = Error.NotFound("BankAccount.NotFound", "Bank account not found.");

    public static readonly Error Duplicate = Error.Conflict(
        "BankAccount.Duplicate", "An account with this bank code, account number and currency already exists.");

    public static readonly Error Inactive = Error.Validation("BankAccount.Inactive", "The bank account is inactive.");

    public static Error Invalid(string field, string reason) => Error.Validation("BankAccount.Invalid", $"{field} {reason}.");
}
