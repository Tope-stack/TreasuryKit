using TreasuryKit.BuildingBlocks.Application.Events;

namespace TreasuryKit.Core.Contracts.ExpectedTransactions;

public enum CashDirection
{
    Inflow,
    Outflow,
}

public sealed record ExpectedTransactionCreated(
    Guid Id,
    DateTimeOffset OccurredAt,
    Guid TenantId,
    Guid ExpectedTransactionId,
    Guid? AccountId,
    CashDirection Direction,
    decimal Amount,
    string Currency,
    DateOnly ExpectedDate) : IIntegrationEvent
{
    Guid? IIntegrationEvent.TenantId => TenantId;
}

public sealed record ExpectedTransactionUpdated(
    Guid Id,
    DateTimeOffset OccurredAt,
    Guid TenantId,
    Guid ExpectedTransactionId,
    Guid? AccountId,
    CashDirection Direction,
    decimal Amount,
    string Currency,
    DateOnly ExpectedDate) : IIntegrationEvent
{
    Guid? IIntegrationEvent.TenantId => TenantId;
}

public sealed record ExpectedTransactionCancelled(
    Guid Id,
    DateTimeOffset OccurredAt,
    Guid TenantId,
    Guid ExpectedTransactionId) : IIntegrationEvent
{
    Guid? IIntegrationEvent.TenantId => TenantId;
}
