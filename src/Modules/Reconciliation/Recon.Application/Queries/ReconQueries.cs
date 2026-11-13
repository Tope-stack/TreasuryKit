using Microsoft.EntityFrameworkCore;
using TreasuryKit.BuildingBlocks.Application.Messaging;
using TreasuryKit.BuildingBlocks.Domain;
using TreasuryKit.Recon.Domain.Exceptions;
using TreasuryKit.Recon.Domain.Matches;
using TreasuryKit.Recon.Domain.Matching;
using TreasuryKit.Recon.Domain.Runs;
using MatchType = TreasuryKit.Recon.Domain.Matching.MatchType;

namespace TreasuryKit.Recon.Application.Queries;

public sealed record RunDto(
    Guid Id,
    Guid AccountId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    RunStatus Status,
    RunTrigger TriggeredBy,
    string RequestedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    RunStats? Stats,
    string? Error,
    bool RerunRequested)
{
    public static RunDto From(ReconciliationRun r) =>
        new(r.Id, r.AccountId, r.PeriodStart, r.PeriodEnd, r.Status, r.TriggeredBy, r.RequestedBy, r.CreatedAt, r.StartedAt,
            r.CompletedAt, r.Stats, r.Error, r.RerunRequested);
}

public sealed record MatchItemDto(ItemType ItemType, Guid ItemId, decimal AllocatedAmount);

public sealed record MatchDto(
    Guid Id,
    Guid AccountId,
    Guid? RunId,
    MatchType Type,
    string RuleKey,
    decimal Confidence,
    IReadOnlyDictionary<string, decimal> ScoreBreakdown,
    MatchStatus Status,
    string Currency,
    DateOnly ValueDate,
    string ProposedBy,
    DateTimeOffset ProposedAt,
    string? ConfirmedBy,
    DateTimeOffset? ConfirmedAt,
    string? Reason,
    IReadOnlyList<MatchItemDto> Items);

public sealed record ExceptionDto(
    Guid Id,
    Guid AccountId,
    Guid RunId,
    ItemType ItemType,
    Guid ItemId,
    ExceptionReason Reason,
    IReadOnlyList<ExceptionCandidate> Candidates,
    ExceptionStatus Status,
    DateTimeOffset CreatedAt);

public sealed record GetRunQuery(Guid Id) : IQuery<RunDto>;

public sealed record ListRunsQuery(Guid? AccountId) : IQuery<IReadOnlyList<RunDto>>;

public sealed record GetMatchQuery(Guid Id) : IQuery<MatchDto>;

public sealed record ListMatchesQuery(MatchStatus? Status, Guid? AccountId, DateOnly? From, DateOnly? To) : IQuery<IReadOnlyList<MatchDto>>;

public sealed record ListExceptionsQuery(ExceptionStatus? Status, Guid? AccountId) : IQuery<IReadOnlyList<ExceptionDto>>;

internal sealed class ReconQueryHandlers(IReconDbContext db) :
    IQueryHandler<GetRunQuery, RunDto>,
    IQueryHandler<ListRunsQuery, IReadOnlyList<RunDto>>,
    IQueryHandler<GetMatchQuery, MatchDto>,
    IQueryHandler<ListMatchesQuery, IReadOnlyList<MatchDto>>,
    IQueryHandler<ListExceptionsQuery, IReadOnlyList<ExceptionDto>>
{
    private const int MaxResults = 5000;

    public async Task<Result<RunDto>> HandleAsync(GetRunQuery query, CancellationToken cancellationToken)
    {
        var run = await db.Runs.AsNoTracking().FirstOrDefaultAsync(r => r.Id == query.Id, cancellationToken);
        return run is null ? RunErrors.NotFound : RunDto.From(run);
    }

    public async Task<Result<IReadOnlyList<RunDto>>> HandleAsync(ListRunsQuery query, CancellationToken cancellationToken)
    {
        var runs = db.Runs.AsNoTracking();
        if (query.AccountId is { } accountId)
        {
            runs = runs.Where(r => r.AccountId == accountId);
        }

        var list = await runs.OrderByDescending(r => r.CreatedAt).Take(200).ToListAsync(cancellationToken);
        return list.Select(RunDto.From).ToList();
    }

    public async Task<Result<MatchDto>> HandleAsync(GetMatchQuery query, CancellationToken cancellationToken)
    {
        var match = await db.Matches.AsNoTracking().Include(m => m.Items).FirstOrDefaultAsync(m => m.Id == query.Id, cancellationToken);
        return match is null ? MatchErrors.NotFound : ToDto(match);
    }

    public async Task<Result<IReadOnlyList<MatchDto>>> HandleAsync(ListMatchesQuery query, CancellationToken cancellationToken)
    {
        var matches = db.Matches.AsNoTracking().Include(m => m.Items).AsQueryable();
        if (query.Status is { } status)
        {
            matches = matches.Where(m => m.Status == status);
        }

        if (query.AccountId is { } accountId)
        {
            matches = matches.Where(m => m.AccountId == accountId);
        }

        if (query.From is { } from)
        {
            matches = matches.Where(m => m.ValueDate >= from);
        }

        if (query.To is { } to)
        {
            matches = matches.Where(m => m.ValueDate <= to);
        }

        var list = await matches.OrderBy(m => m.ValueDate).ThenBy(m => m.Id).Take(MaxResults).ToListAsync(cancellationToken);
        return list.Select(ToDto).ToList();
    }

    public async Task<Result<IReadOnlyList<ExceptionDto>>> HandleAsync(ListExceptionsQuery query, CancellationToken cancellationToken)
    {
        var exceptions = db.Exceptions.AsNoTracking();
        if (query.Status is { } status)
        {
            exceptions = exceptions.Where(e => e.Status == status);
        }

        if (query.AccountId is { } accountId)
        {
            exceptions = exceptions.Where(e => e.AccountId == accountId);
        }

        var list = await exceptions.OrderBy(e => e.CreatedAt).ThenBy(e => e.Id).Take(MaxResults).ToListAsync(cancellationToken);
        return list
            .Select(e => new ExceptionDto(e.Id, e.AccountId, e.RunId, e.ItemType, e.ItemId, e.Reason, e.Candidates, e.Status, e.CreatedAt))
            .ToList();
    }

    private static MatchDto ToDto(Match m) =>
        new(m.Id, m.AccountId, m.RunId, m.Type, m.RuleKey, m.Confidence, m.ScoreBreakdown, m.Status, m.Currency.Code, m.ValueDate,
            m.ProposedBy, m.ProposedAt, m.ConfirmedBy, m.ConfirmedAt, m.Reason,
            m.Items.Select(i => new MatchItemDto(i.ItemType, i.ItemId, i.AllocatedAmount.Amount)).ToList());
}
