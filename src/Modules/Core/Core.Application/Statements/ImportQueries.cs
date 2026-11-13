using Microsoft.EntityFrameworkCore;
using TreasuryKit.BuildingBlocks.Application.Abstractions;
using TreasuryKit.BuildingBlocks.Application.Messaging;
using TreasuryKit.BuildingBlocks.Domain;
using TreasuryKit.Core.Application.Common;
using TreasuryKit.Core.Domain.Accounts;
using TreasuryKit.Core.Domain.Statements;

namespace TreasuryKit.Core.Application.Statements;

public sealed record StatementImportDto(
    Guid Id,
    Guid AccountId,
    string FileName,
    string FileHash,
    StatementFormat Format,
    StatementImportStatus Status,
    string Currency,
    DateOnly? PeriodStart,
    DateOnly? PeriodEnd,
    decimal? OpeningBalance,
    decimal? ClosingBalance,
    int LineCount,
    int DuplicateLineCount,
    bool BalanceValidated,
    string? Error,
    string ImportedBy,
    DateTimeOffset ImportedAt,
    DateTimeOffset? CompletedAt,
    string? AcknowledgedBy,
    DateTimeOffset? AcknowledgedAt);

public sealed record StatementLineDto(
    Guid Id,
    Guid ImportId,
    int RowNumber,
    DateOnly BookingDate,
    DateOnly ValueDate,
    decimal Amount,
    string Currency,
    string Narration,
    string? BankReference,
    decimal? RunningBalance);

public sealed record GetStatementImportQuery(Guid Id) : IQuery<StatementImportDto>;

public sealed record GetStatementLinesQuery(Guid AccountId, DateOnly? From, DateOnly? To, int Page, int PageSize)
    : IQuery<PagedResult<StatementLineDto>>;

public sealed record AcknowledgeBalanceMismatchCommand(Guid ImportId) : ICommand;

internal sealed class GetStatementImportHandler(ICoreDbContext db) : IQueryHandler<GetStatementImportQuery, StatementImportDto>
{
    public async Task<Result<StatementImportDto>> HandleAsync(GetStatementImportQuery query, CancellationToken cancellationToken)
    {
        var i = await db.StatementImports.AsNoTracking().FirstOrDefaultAsync(x => x.Id == query.Id, cancellationToken);
        return i is null
            ? StatementImportErrors.NotFound
            : new StatementImportDto(
                i.Id, i.AccountId, i.FileName, i.FileHash, i.Format, i.Status, i.Currency.Code, i.PeriodStart, i.PeriodEnd,
                i.OpeningBalance, i.ClosingBalance, i.LineCount, i.DuplicateLineCount, i.BalanceValidated, i.Error,
                i.ImportedBy, i.ImportedAt, i.CompletedAt, i.AcknowledgedBy, i.AcknowledgedAt);
    }
}

internal sealed class GetStatementLinesHandler(ICoreDbContext db) : IQueryHandler<GetStatementLinesQuery, PagedResult<StatementLineDto>>
{
    public const int MaxPageSize = 500;

    public async Task<Result<PagedResult<StatementLineDto>>> HandleAsync(GetStatementLinesQuery query, CancellationToken cancellationToken)
    {
        if (query.Page < 1 || query.PageSize is < 1 or > MaxPageSize)
        {
            return Error.Validation("Paging.Invalid", $"page must be ≥ 1 and pageSize between 1 and {MaxPageSize}.");
        }

        if (!await db.BankAccounts.AnyAsync(a => a.Id == query.AccountId, cancellationToken))
        {
            return BankAccountErrors.NotFound;
        }

        var lines = db.StatementLines.AsNoTracking().Where(l => l.AccountId == query.AccountId);
        if (query.From is { } from)
        {
            lines = lines.Where(l => l.ValueDate >= from);
        }

        if (query.To is { } to)
        {
            lines = lines.Where(l => l.ValueDate <= to);
        }

        var total = await lines.CountAsync(cancellationToken);
        var page = await lines
            .OrderBy(l => l.ValueDate).ThenBy(l => l.ImportId).ThenBy(l => l.RowNumber)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        var items = page
            .Select(l => new StatementLineDto(
                l.Id, l.ImportId, l.RowNumber, l.BookingDate, l.ValueDate, l.Amount.Amount, l.Amount.Currency.Code,
                l.Narration, l.BankReference, l.RunningBalance))
            .ToList();
        return new PagedResult<StatementLineDto>(items, query.Page, query.PageSize, total);
    }
}

internal sealed class AcknowledgeBalanceMismatchHandler(ICoreDbContext db, ICurrentUser user, IClock clock)
    : ICommandHandler<AcknowledgeBalanceMismatchCommand>
{
    public async Task<Result> HandleAsync(AcknowledgeBalanceMismatchCommand command, CancellationToken cancellationToken)
    {
        var import = await db.StatementImports.FirstOrDefaultAsync(i => i.Id == command.ImportId, cancellationToken);
        if (import is null)
        {
            return StatementImportErrors.NotFound;
        }

        var now = clock.UtcNow;
        var result = import.AcknowledgeMismatch(user.Required(), now);
        if (result.IsFailure)
        {
            return result;
        }

        db.Publish(StatementImportEvents.Imported(import, import.LineCount - import.DuplicateLineCount, now));
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
