using TreasuryKit.BuildingBlocks.Application.Events;

namespace TreasuryKit.Recon.Contracts.Matches;

public enum MatchedItemType
{
    StatementLine,
    ExpectedTransaction,
}

/// <param name="AllocatedAmount">Positive amount of the item covered by the match.</param>
public sealed record MatchedItem(MatchedItemType Type, Guid ItemId, decimal AllocatedAmount, string Currency);

/// <summary>A match became confirmed (auto-confirmed by a run, or approved). Core settles expected transactions from it.</summary>
public sealed record MatchConfirmed(
    Guid Id,
    DateTimeOffset OccurredAt,
    Guid TenantId,
    Guid MatchId,
    Guid AccountId,
    IReadOnlyList<MatchedItem> Items) : IIntegrationEvent
{
    Guid? IIntegrationEvent.TenantId => TenantId;
}

/// <summary>A confirmed match was reversed; its items are back in the pool.</summary>
public sealed record MatchReversed(
    Guid Id,
    DateTimeOffset OccurredAt,
    Guid TenantId,
    Guid MatchId,
    Guid AccountId,
    IReadOnlyList<MatchedItem> Items) : IIntegrationEvent
{
    Guid? IIntegrationEvent.TenantId => TenantId;
}

public sealed record RunStatistics(
    int LinesConsidered,
    int ExpectedConsidered,
    int AutoConfirmed,
    int Proposed,
    int Exceptions,
    int Superseded,
    long DurationMs);

public sealed record ReconciliationRunCompleted(
    Guid Id,
    DateTimeOffset OccurredAt,
    Guid TenantId,
    Guid AccountId,
    Guid RunId,
    RunStatistics Stats) : IIntegrationEvent
{
    Guid? IIntegrationEvent.TenantId => TenantId;
}
