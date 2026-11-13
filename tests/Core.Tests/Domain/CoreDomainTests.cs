using TreasuryKit.BuildingBlocks.Domain;
using TreasuryKit.Core.Domain.Accounts;
using TreasuryKit.Core.Domain.Counterparties;
using TreasuryKit.Core.Domain.MappingProfiles;
using TreasuryKit.Core.Domain.Statements;

namespace TreasuryKit.Core.Tests.Domain;

public sealed class CoreDomainTests
{
    private static readonly Guid Tenant = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Bank_account_normalizes_and_defaults_display_name()
    {
        var account = BankAccount.Create(Tenant, " 058 ", "Demo Bank", "0123 456 789", "ngn", null, null, Now).Value;

        Assert.Equal("058", account.BankCode);
        Assert.Equal("0123456789", account.AccountNumber);
        Assert.Equal(Currency.NGN, account.Currency);
        Assert.Equal("Demo Bank 0123456789 (NGN)", account.DisplayName);
        Assert.True(account.IsActive);
    }

    [Theory]
    [InlineData("", "Bank", "0123456789", "NGN")]
    [InlineData("058", "", "0123456789", "NGN")]
    [InlineData("058", "Bank", "01-23", "NGN")]
    [InlineData("058", "Bank", "0123456789", "XYZ")]
    public void Bank_account_rejects_invalid_input(string code, string name, string number, string currency) =>
        Assert.True(BankAccount.Create(Tenant, code, name, number, currency, null, null, Now).IsFailure);

    [Fact]
    public void Counterparty_normalizes_name_and_aliases()
    {
        var counterparty = Counterparty.Create(Tenant, "Okonkwo & Sons Ltd.", ["OKONKWO AND SONS", "okonkwo & sons ltd"], "ng", "NGN").Value;

        Assert.Equal("OKONKWO SONS LTD", counterparty.NormalizedName);
        Assert.Equal(["OKONKWO AND SONS"], counterparty.Aliases);
        Assert.Equal("NG", counterparty.Country);
    }

    [Fact]
    public void Mapping_profile_requires_columns_for_its_amount_mode()
    {
        var map = new ColumnMap(["Date"], null, ["Narration"], null, Amount: null, Debit: null, Credit: null, null, null);

        var result = BankMappingProfile.Create(Tenant, "058", "P", TabularFileType.Csv, 0, 0, ",", ["dd/MM/yyyy"], "en-NG", map, AmountMode.Signed, null, null);

        Assert.True(result.IsFailure);
        Assert.Contains("columnMap.amount", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Mapping_profile_rejects_unknown_culture_and_missing_delimiter()
    {
        var map = new ColumnMap(["Date"], null, ["Narration"], null, ["Amount"], null, null, null, null);

        var result = BankMappingProfile.Create(Tenant, "058", "P", TabularFileType.Csv, 0, 0, null, ["dd/MM/yyyy"], "xx-NOPE", map, AmountMode.Signed, null, null);

        Assert.Contains("delimiter", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("culture", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_completes_or_flags_mismatch()
    {
        var balanced = NewImport();
        balanced.StartParsing();
        balanced.Complete(null, null, 0m, 10m, 2, 1, BalanceCheck.Balanced, Now);
        Assert.Equal(StatementImportStatus.Completed, balanced.Status);
        Assert.Equal(1, balanced.DuplicateLineCount);
        Assert.True(balanced.BalanceValidated);

        var mismatched = NewImport();
        mismatched.Complete(null, null, 0m, 11m, 2, 0, new BalanceCheck(BalanceCheckOutcome.Mismatch, "off by 1"), Now);
        Assert.Equal(StatementImportStatus.BalanceMismatch, mismatched.Status);
        Assert.Equal("off by 1", mismatched.Error);
    }

    [Fact]
    public void Only_mismatched_imports_can_be_acknowledged()
    {
        var import = NewImport();
        Assert.True(import.AcknowledgeMismatch("admin", Now).IsFailure);

        import.Complete(null, null, 0m, 11m, 1, 0, new BalanceCheck(BalanceCheckOutcome.Mismatch, "x"), Now);
        Assert.True(import.AcknowledgeMismatch("admin", Now).IsSuccess);
        Assert.Equal(StatementImportStatus.Completed, import.Status);
        Assert.Equal("admin", import.AcknowledgedBy);
    }

    [Fact]
    public void Finished_import_cannot_be_processed_again()
    {
        var import = NewImport();
        import.Fail("bad file", Now);

        Assert.False(import.IsPending);
        Assert.Throws<InvalidOperationException>(import.StartParsing);
    }

    private static StatementImport NewImport() =>
        StatementImport.Queue(Tenant, Guid.CreateVersion7(), Currency.NGN, "s.csv", new string('a', 64), "k", StatementFormat.Csv, "ada", Now);
}
