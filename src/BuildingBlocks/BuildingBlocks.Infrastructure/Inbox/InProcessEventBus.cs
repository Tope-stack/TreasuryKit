using Microsoft.Extensions.DependencyInjection;
using TreasuryKit.BuildingBlocks.Application.Events;
using TreasuryKit.BuildingBlocks.Infrastructure.Context;

namespace TreasuryKit.BuildingBlocks.Infrastructure.Inbox;

/// <summary>
/// Delivers an event to every subscriber, each in its own DI scope with the event's tenant as the current tenant.
/// All subscribers are attempted; if any fail, the publish fails and the outbox redelivers later —
/// subscribers that already succeeded are skipped by their inbox.
/// </summary>
internal sealed class InProcessEventBus(IServiceScopeFactory scopes, IEnumerable<IntegrationEventSubscription> subscriptions)
    : IEventBus
{
    private readonly ILookup<Type, IntegrationEventSubscription> _byEvent = subscriptions.ToLookup(s => s.EventType);

    public async Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        List<Exception>? failures = null;

        foreach (var subscription in _byEvent[integrationEvent.GetType()])
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<TenantContext>().Set(integrationEvent.TenantId);
                scope.ServiceProvider.GetRequiredService<UserContext>().SetSystem();

                var invoker = (IIntegrationEventInvoker)scope.ServiceProvider.GetRequiredService(subscription.InvokerType);
                await invoker.InvokeAsync(integrationEvent, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                (failures ??= []).Add(ex);
            }
        }

        if (failures is not null)
        {
            throw new AggregateException($"{failures.Count} subscriber(s) failed for event {integrationEvent.Id}.", failures);
        }
    }
}
