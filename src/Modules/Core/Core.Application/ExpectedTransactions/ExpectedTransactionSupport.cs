using Microsoft.EntityFrameworkCore;
using TreasuryKit.BuildingBlocks.Domain;
using TreasuryKit.Core.Contracts.ExpectedTransactions;
using TreasuryKit.Core.Domain.Accounts;
using TreasuryKit.Core.Domain.Common;
using TreasuryKit.Core.Domain.ExpectedTransactions;

namespace TreasuryKit.Core.Application.ExpectedTransactions;

public sealed record ExpectedTransactionDto(
    Guid Id,
    Guid? AccountId,
    TransactionDirection Direction,
    decimal Amount,
    decimal OutstandingAmount,
    string Currency,
    DateOnly ExpectedDate,
    Guid? CounterpartyId,
    string? CounterpartyName,
    string? Reference,
    string? Description,
    ExpectedTransactionSource Source,
    string? ExternalId,
    ExpectedTransactionStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt)
{
    public static ExpectedTransactionDto From(ExpectedTransaction t) =>
        new(t.Id, t.AccountId, t.Direction, t.Amount.Amount, t.OutstandingAmount, t.Amount.Currency.Code, t.ExpectedDate,
            t.CounterpartyId, t.CounterpartyName, t.Reference, t.Description, t.Source, t.ExternalId, t.Status, t.CreatedAt, t.UpdatedAt);
}

/// <summary>Raw input from the API or a CSV row, before validation.</summary>
public sealed record ExpectedTransactionInput(
    Guid? AccountId,
    string? AccountNumber,
    string? Direction,
    decimal? Amount,
    string? Currency,
    DateOnly? ExpectedDate,
    string? CounterpartyName,
    string? Reference,
    string? Description);

/// <summary>Validates input against Core data: account, currency, counterparty lookup.</summary>
internal sealed class ExpectedTransactionInputResolver(ICoreDbContext db)
{
    public async Task<Result<ExpectedTransactionDetails>> ResolveAsync(ExpectedTransactionInput input, CancellationToken ct)
    {
        if (!Enum.TryParse<TransactionDirection>(input.Direction?.Trim(), ignoreCase: true, out var direction)
            || !Enum.IsDefined(direction))
        {
            return Invalid($"direction must be Inflow or Outflow (got '{input.Direction}')");
        }

        if (input.Amount is null)
        {
            return Invalid("amount is required");
        }

        if (input.ExpectedDate is null)
        {
            return Invalid("expectedDate is required");
        }

        BankAccount? account = null;
        if (input.AccountId is { } accountId)
        {
            account = await db.BankAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId, ct);
            if (account is null)
            {
                return BankAccountErrors.NotFound;
            }
        }
        else if (!string.IsNullOrWhiteSpace(input.AccountNumber))
        {
            var number = input.AccountNumber.Replace(" ", string.Empty, StringComparison.Ordinal).Trim();
            var candidates = await db.BankAccounts.AsNoTracking().Where(a => a.AccountNumber == number).ToListAsync(ct);
            if (candidates.Count > 1 && input.Currency is not null)
            {
                candidates = candidates.Where(a => string.Equals(a.Currency.Code, input.Currency.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
            }

            if (candidates.Count != 1)
            {
                return Invalid(candidates.Count == 0
                    ? $"no bank account with number '{number}'"
                    : $"account number '{number}' is ambiguous; give the currency");
            }

            account = candidates[0];
        }

        var currency = string.IsNullOrWhiteSpace(input.Currency) && account is not null
            ? Result.Success(account.Currency)
            : Currency.FromCode(input.Currency);
        if (currency.IsFailure)
        {
            return currency.Error;
        }

        if (account is not null && account.Currency != currency.Value)
        {
            return Invalid($"currency {currency.Value.Code} differs from the account's currency {account.Currency.Code}");
        }

        Guid? counterpartyId = null;
        if (!string.IsNullOrWhiteSpace(input.CounterpartyName))
        {
            var normalized = TextNormalization.ForName(input.CounterpartyName);
            counterpartyId = await db.Counterparties.AsNoTracking()
                .Where(c => c.NormalizedName == normalized || c.Aliases.Contains(normalized))
                .Select(c => (Guid?)c.Id)
                .FirstOrDefaultAsync(ct);
        }

        return new ExpectedTransactionDetails(
            account?.Id,
            direction,
            input.Amount.Value,
            currency.Value,
            input.ExpectedDate.Value,
            counterpartyId,
            input.CounterpartyName,
            input.Reference,
            input.Description);
    }

    private static Error Invalid(string reason) => Error.Validation("ExpectedTransaction.Invalid", reason + ".");
}

internal static class ExpectedTransactionEvents
{
    public static ExpectedTransactionCreated Created(ExpectedTransaction t, DateTimeOffset now) =>
        new(Guid.CreateVersion7(), now, t.TenantId, t.Id, t.AccountId, Map(t.Direction), t.Amount.Amount, t.Amount.Currency.Code, t.ExpectedDate);

    public static ExpectedTransactionUpdated Updated(ExpectedTransaction t, DateTimeOffset now) =>
        new(Guid.CreateVersion7(), now, t.TenantId, t.Id, t.AccountId, Map(t.Direction), t.Amount.Amount, t.Amount.Currency.Code, t.ExpectedDate);

    public static ExpectedTransactionCancelled Cancelled(ExpectedTransaction t, DateTimeOffset now) =>
        new(Guid.CreateVersion7(), now, t.TenantId, t.Id);

    public static CashDirection Map(TransactionDirection direction) =>
        direction == TransactionDirection.Inflow ? CashDirection.Inflow : CashDirection.Outflow;
}
