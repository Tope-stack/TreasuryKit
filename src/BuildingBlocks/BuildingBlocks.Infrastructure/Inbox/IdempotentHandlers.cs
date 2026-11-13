using Microsoft.EntityFrameworkCore;
using TreasuryKit.BuildingBlocks.Application.Abstractions;
using TreasuryKit.BuildingBlocks.Application.Events;
using TreasuryKit.BuildingBlocks.Infrastructure.Persistence;

namespace TreasuryKit.BuildingBlocks.Infrastructure.Inbox;

/// <summary>Links an event type to the invoker that runs one subscriber.</summary>
public sealed record IntegrationEventSubscription(Type EventType, Type InvokerType);

public interface IIntegrationEventInvoker
{
    Task InvokeAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken);
}

/// <summary>
/// Runs <typeparamref name="THandler"/> at most once per message: the handler's writes and the inbox record
/// are committed in one transaction on the subscribing module's DbContext.
/// </summary>
internal sealed class InboxInvoker<TEvent, THandler, TContext>(THandler handler, TContext db, IClock clock)
    : IIntegrationEventInvoker
    where TEvent : IIntegrationEvent
    where THandler : IIntegrationEventHandler<TEvent>
    where TContext : ModuleDbContext
{
    private static readonly string HandlerName = typeof(THandler).FullName!;

    public async Task InvokeAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var alreadyProcessed = await db.InboxMessages.AnyAsync(
            m => m.MessageId == integrationEvent.Id && m.Handler == HandlerName,
            cancellationToken);
        if (alreadyProcessed)
        {
            return;
        }

        await handler.HandleAsync((TEvent)integrationEvent, cancellationToken);

        db.InboxMessages.Add(new InboxMessage(integrationEvent.Id, HandlerName, clock.UtcNow));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
