namespace TreasuryKit.BuildingBlocks.Application.Events;

/// <summary>
/// A fact published by one module for others. Written to the publisher's outbox in the same transaction
/// as the state change and delivered asynchronously. Types live in a module's Contracts project.
/// </summary>
public interface IIntegrationEvent
{
    Guid Id { get; }

    DateTimeOffset OccurredAt { get; }

    /// <summary>The tenant the event belongs to; handlers run with this tenant as the current tenant.</summary>
    Guid? TenantId { get; }
}

public interface IIntegrationEventHandler<in TEvent>
    where TEvent : IIntegrationEvent
{
    Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken);
}

/// <summary>Delivers an integration event to its subscribers. In-process today; swappable for a broker.</summary>
public interface IEventBus
{
    Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken);
}
