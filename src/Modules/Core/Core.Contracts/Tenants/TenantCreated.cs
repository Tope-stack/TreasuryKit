using TreasuryKit.BuildingBlocks.Application.Events;

namespace TreasuryKit.Core.Contracts.Tenants;

/// <summary>Published when a tenant is created. Recon seeds its default matching rules from this (Phase 3).</summary>
public sealed record TenantCreated(
    Guid Id,
    DateTimeOffset OccurredAt,
    Guid TenantId,
    string Name,
    string BusinessTimeZone) : IIntegrationEvent
{
    Guid? IIntegrationEvent.TenantId => TenantId;
}
