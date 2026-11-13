using System.Collections.Concurrent;
using TreasuryKit.BuildingBlocks.Application.Abstractions;
using TreasuryKit.BuildingBlocks.Application.Events;

namespace TreasuryKit.IntegrationTests.Outbox;

public sealed record TestEvent(Guid Id, DateTimeOffset OccurredAt, Guid? TenantId, bool FailFirstDelivery) : IIntegrationEvent;

/// <summary>Records handler invocations per (handler, event) across scopes.</summary>
public sealed class HandlerCallLog
{
    private readonly ConcurrentDictionary<(string Handler, Guid EventId), int> _calls = new();

    public ConcurrentDictionary<Guid, Guid?> TenantSeen { get; } = new();

    public int Record(string handler, Guid eventId) => _calls.AddOrUpdate((handler, eventId), 1, (_, n) => n + 1);

    public int Count(string handler, Guid eventId) => _calls.GetValueOrDefault((handler, eventId));
}

public sealed class CountingHandler(HandlerCallLog log, ICurrentTenant tenant) : IIntegrationEventHandler<TestEvent>
{
    public Task HandleAsync(TestEvent integrationEvent, CancellationToken cancellationToken)
    {
        log.Record(nameof(CountingHandler), integrationEvent.Id);
        log.TenantSeen[integrationEvent.Id] = tenant.TenantId;
        return Task.CompletedTask;
    }
}

/// <summary>Throws on the first delivery of events flagged <see cref="TestEvent.FailFirstDelivery"/>.</summary>
public sealed class FlakyHandler(HandlerCallLog log) : IIntegrationEventHandler<TestEvent>
{
    public Task HandleAsync(TestEvent integrationEvent, CancellationToken cancellationToken)
    {
        var attempt = log.Record(nameof(FlakyHandler), integrationEvent.Id);
        if (integrationEvent.FailFirstDelivery && attempt == 1)
        {
            throw new InvalidOperationException("Simulated transient failure.");
        }

        return Task.CompletedTask;
    }
}
