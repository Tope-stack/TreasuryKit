using TreasuryKit.BuildingBlocks.Application.Events;

namespace TreasuryKit.Core.Contracts.Statements;

/// <summary>
/// Published when an import's lines become available for reconciliation: on completion, or when an Admin
/// acknowledges a balance mismatch. Recon triggers an automatic run from this (Phase 3).
/// </summary>
public sealed record StatementImported(
    Guid Id,
    DateTimeOffset OccurredAt,
    Guid TenantId,
    Guid AccountId,
    Guid ImportId,
    DateOnly? PeriodStart,
    DateOnly? PeriodEnd,
    int NewLineCount) : IIntegrationEvent
{
    Guid? IIntegrationEvent.TenantId => TenantId;
}
