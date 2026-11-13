using Microsoft.EntityFrameworkCore;
using TreasuryKit.BuildingBlocks.Application.Abstractions;
using TreasuryKit.BuildingBlocks.Application.Messaging;
using TreasuryKit.BuildingBlocks.Domain;
using TreasuryKit.Core.Application.Common;
using TreasuryKit.Core.Domain.Accounts;
using TreasuryKit.Core.Domain.MappingProfiles;

namespace TreasuryKit.Core.Application.Accounts;

public sealed record BankAccountDto(
    Guid Id,
    string BankCode,
    string BankName,
    string AccountNumber,
    string Currency,
    string DisplayName,
    Guid? MappingProfileId,
    bool IsActive,
    DateTimeOffset CreatedAt);

public sealed record CreateBankAccountCommand(
    string BankCode,
    string BankName,
    string AccountNumber,
    string Currency,
    string? DisplayName,
    Guid? MappingProfileId) : ICommand<Guid>;

public sealed record ListBankAccountsQuery : IQuery<IReadOnlyList<BankAccountDto>>;

public sealed record GetBankAccountQuery(Guid Id) : IQuery<BankAccountDto>;

internal sealed class CreateBankAccountHandler(ICoreDbContext db, ICurrentTenant tenant, IClock clock)
    : ICommandHandler<CreateBankAccountCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateBankAccountCommand command, CancellationToken cancellationToken)
    {
        if (command.MappingProfileId is { } profileId
            && !await db.MappingProfiles.AnyAsync(p => p.Id == profileId, cancellationToken))
        {
            return MappingProfileErrors.NotFound;
        }

        var created = BankAccount.Create(
            tenant.Required(),
            command.BankCode,
            command.BankName,
            command.AccountNumber,
            command.Currency,
            command.DisplayName,
            command.MappingProfileId,
            clock.UtcNow);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var account = created.Value;
        if (await IsDuplicateAsync(account, cancellationToken))
        {
            return BankAccountErrors.Duplicate;
        }

        db.BankAccounts.Add(account);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (await IsDuplicateAsync(account, cancellationToken))
            {
                return BankAccountErrors.Duplicate; // lost a race with a concurrent create
            }

            throw;
        }

        return account.Id;
    }

    private Task<bool> IsDuplicateAsync(BankAccount account, CancellationToken ct) =>
        db.BankAccounts.AnyAsync(
            a => a.Id != account.Id
                && a.BankCode == account.BankCode
                && a.AccountNumber == account.AccountNumber
                && a.Currency == account.Currency,
            ct);
}

internal sealed class ListBankAccountsHandler(ICoreDbContext db) : IQueryHandler<ListBankAccountsQuery, IReadOnlyList<BankAccountDto>>
{
    public async Task<Result<IReadOnlyList<BankAccountDto>>> HandleAsync(ListBankAccountsQuery query, CancellationToken cancellationToken)
    {
        var accounts = await db.BankAccounts.AsNoTracking().OrderBy(a => a.DisplayName).ToListAsync(cancellationToken);
        return accounts.Select(BankAccountMapping.ToDto).ToList();
    }
}

internal sealed class GetBankAccountHandler(ICoreDbContext db) : IQueryHandler<GetBankAccountQuery, BankAccountDto>
{
    public async Task<Result<BankAccountDto>> HandleAsync(GetBankAccountQuery query, CancellationToken cancellationToken)
    {
        var account = await db.BankAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == query.Id, cancellationToken);
        return account is null ? BankAccountErrors.NotFound : BankAccountMapping.ToDto(account);
    }
}

internal static class BankAccountMapping
{
    public static BankAccountDto ToDto(BankAccount a) =>
        new(a.Id, a.BankCode, a.BankName, a.AccountNumber, a.Currency.Code, a.DisplayName, a.MappingProfileId, a.IsActive, a.CreatedAt);
}
