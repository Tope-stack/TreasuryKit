using System.Text.Json;
using TreasuryKit.BuildingBlocks.Application.Events;
using TreasuryKit.BuildingBlocks.Infrastructure.Auditing;
using TreasuryKit.BuildingBlocks.Infrastructure.Persistence;

namespace TreasuryKit.BuildingBlocks.Infrastructure.Outbox;

public sealed class OutboxMessage : INotAudited
{
    private OutboxMessage()
    {
    }

    public Guid Id { get; private set; }

    public string Type { get; private set; } = null!;

    public string Content { get; private set; } = null!;

    public DateTimeOffset OccurredAt { get; private set; }

    public Guid? TenantId { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public int Attempts { get; private set; }

    public string? LastError { get; private set; }

    public static OutboxMessage Create(IIntegrationEvent integrationEvent)
    {
        var type = integrationEvent.GetType();
        return new OutboxMessage
        {
            Id = integrationEvent.Id,
            Type = IntegrationEventSerializer.TypeName(type),
            Content = JsonSerializer.Serialize(integrationEvent, type, IntegrationEventSerializer.Options),
            OccurredAt = integrationEvent.OccurredAt,
            TenantId = integrationEvent.TenantId,
        };
    }

    public void MarkProcessed(DateTimeOffset at)
    {
        Attempts++;
        ProcessedAt = at;
        LastError = null;
    }

    public void RecordFailure(string error)
    {
        Attempts++;
        LastError = error.Length > 4000 ? error[..4000] : error;
    }
}
