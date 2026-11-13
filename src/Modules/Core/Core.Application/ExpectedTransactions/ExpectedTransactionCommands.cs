using Microsoft.EntityFrameworkCore;
using TreasuryKit.BuildingBlocks.Application.Abstractions;
using TreasuryKit.BuildingBlocks.Application.Messaging;
using TreasuryKit.BuildingBlocks.Domain;
using TreasuryKit.Core.Application.Common;
using TreasuryKit.Core.Domain.ExpectedTransactions;

namespace TreasuryKit.Core.Application.ExpectedTransactions;

/// <param name="ExternalId">Optional caller id; repeating a create with the same id and data returns the existing transaction.</param>
public sealed record CreateExpectedTransactionCommand(
    Guid? AccountId,
    string Direction,
    decimal Amount,
    string? Currency,
    DateOnly ExpectedDate,
    string? CounterpartyName,
    string? Reference,
    string? Description,
    string? ExternalId) : ICommand<CreateExpectedTransactionResult>;

public sealed record CreateExpectedTransactionResult(Guid Id, bool AlreadyExisted);

/// <summary>Partial update: null fields keep their current value.</summary>
public sealed record UpdateExpectedTransactionCommand(
    Guid Id,
    Guid? AccountId,
    string? Direction,
    decimal? Amount,
    string? Currency,
    DateOnly? ExpectedDate,
    string? CounterpartyName,
    string? Reference,
    string? Description) : ICommand;

public sealed record CancelExpectedTransactionCommand(Guid Id) : ICommand;

public sealed record ListExpectedTransactionsQuery(
    ExpectedTransactionStatus? Status,
    DateOnly? From,
    DateOnly? To,
    string? Currency,
    Guid? AccountId) : IQuery<IReadOnlyList<ExpectedTransactionDto>>;

public sealed record GetExpectedTransactionQuery(Guid Id) : IQuery<ExpectedTransactionDto>;

internal sealed class CreateExpectedTransactionHandler(ICoreDbContext db, ICurrentTenant tenant, IClock clock)
    : ICommandHandler<CreateExpectedTransactionCommand, CreateExpectedTransactionResult>
{
    public async Task<Result<CreateExpectedTransactionResult>> HandleAsync(CreateExpectedTransactionCommand c, CancellationToken cancellationToken)
    {
        var details = await new ExpectedTransactionInputResolver(db).ResolveAsync(
            new ExpectedTransactionInput(c.AccountId, null, c.Direction, c.Amount, c.Currency, c.ExpectedDate, c.CounterpartyName, c.Reference, c.Description),
            cancellationToken);
        if (details.IsFailure)
        {
            return details.Error;
        }

        var externalId = string.IsNullOrWhiteSpace(c.ExternalId) ? null : c.ExternalId.Trim();
        if (externalId is not null
            && await db.ExpectedTransactions.FirstOrDefaultAsync(
                t => t.Source == ExpectedTransactionSource.Api && t.ExternalId == externalId, cancellationToken) is { } existing)
        {
            return existing.Matches(details.Value)
                ? new CreateExpectedTransactionResult(existing.Id, AlreadyExisted: true)
                : Error.Conflict("ExpectedTransaction.ExternalIdInUse", $"externalId '{externalId}' already identifies a different transaction.");
        }

        var now = clock.UtcNow;
        var created = ExpectedTransaction.Create(tenant.Required(), details.Value, ExpectedTransactionSource.Api, externalId, now);
        if (created.IsFailure)
        {
            return created.Error;
        }

        db.ExpectedTransactions.Add(created.Value);
        db.Publish(ExpectedTransactionEvents.Created(created.Value, now));
        await db.SaveChangesAsync(cancellationToken);
        return new CreateExpectedTransactionResult(created.Value.Id, AlreadyExisted: false);
    }
}

internal sealed class UpdateExpectedTransactionHandler(ICoreDbContext db, IClock clock) : ICommandHandler<UpdateExpectedTransactionCommand>
{
    public async Task<Result> HandleAsync(UpdateExpectedTransactionCommand c, CancellationToken cancellationToken)
    {
        var transaction = await db.ExpectedTransactions.FirstOrDefaultAsync(t => t.Id == c.Id, cancellationToken);
        if (transaction is null)
        {
            return ExpectedTransactionErrors.NotFound;
        }

        var details = await new ExpectedTransactionInputResolver(db).ResolveAsync(
            new ExpectedTransactionInput(
                c.AccountId ?? transaction.AccountId,
                null,
                c.Direction ?? transaction.Direction.ToString(),
                c.Amount ?? transaction.Amount.Amount,
                c.Currency ?? transaction.Amount.Currency.Code,
                c.ExpectedDate ?? transaction.ExpectedDate,
                c.CounterpartyName ?? transaction.CounterpartyName,
                c.Reference ?? transaction.Reference,
                c.Description ?? transaction.Description),
            cancellationToken);
        if (details.IsFailure)
        {
            return details.Error;
        }

        var now = clock.UtcNow;
        var updated = transaction.Update(details.Value, now);
        if (updated.IsFailure)
        {
            return updated;
        }

        db.Publish(ExpectedTransactionEvents.Updated(transaction, now));
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class CancelExpectedTransactionHandler(ICoreDbContext db, IClock clock) : ICommandHandler<CancelExpectedTransactionCommand>
{
    public async Task<Result> HandleAsync(CancelExpectedTransactionCommand c, CancellationToken cancellationToken)
    {
        var transaction = await db.ExpectedTransactions.FirstOrDefaultAsync(t => t.Id == c.Id, cancellationToken);
        if (transaction is null)
        {
            return ExpectedTransactionErrors.NotFound;
        }

        var now = clock.UtcNow;
        var cancelled = transaction.Cancel(now);
        if (cancelled.IsFailure)
        {
            return cancelled;
        }

        db.Publish(ExpectedTransactionEvents.Cancelled(transaction, now));
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class ListExpectedTransactionsHandler(ICoreDbContext db)
    : IQueryHandler<ListExpectedTransactionsQuery, IReadOnlyList<ExpectedTransactionDto>>
{
    public async Task<Result<IReadOnlyList<ExpectedTransactionDto>>> HandleAsync(ListExpectedTransactionsQuery q, CancellationToken cancellationToken)
    {
        var query = db.ExpectedTransactions.AsNoTracking();
        if (q.Status is { } status)
        {
            query = query.Where(t => t.Status == status);
        }

        if (q.From is { } from)
        {
            query = query.Where(t => t.ExpectedDate >= from);
        }

        if (q.To is { } to)
        {
            query = query.Where(t => t.ExpectedDate <= to);
        }

        if (q.AccountId is { } accountId)
        {
            query = query.Where(t => t.AccountId == accountId);
        }

        if (!string.IsNullOrWhiteSpace(q.Currency))
        {
            var currency = Currency.FromCode(q.Currency);
            if (currency.IsFailure)
            {
                return currency.Error;
            }

            query = query.Where(t => t.Amount.Currency == currency.Value);
        }

        var list = await query.OrderBy(t => t.ExpectedDate).ThenBy(t => t.Id).Take(5000).ToListAsync(cancellationToken);
        return list.Select(ExpectedTransactionDto.From).ToList();
    }
}

internal sealed class GetExpectedTransactionHandler(ICoreDbContext db) : IQueryHandler<GetExpectedTransactionQuery, ExpectedTransactionDto>
{
    public async Task<Result<ExpectedTransactionDto>> HandleAsync(GetExpectedTransactionQuery q, CancellationToken cancellationToken)
    {
        var t = await db.ExpectedTransactions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == q.Id, cancellationToken);
        return t is null ? ExpectedTransactionErrors.NotFound : ExpectedTransactionDto.From(t);
    }
}
