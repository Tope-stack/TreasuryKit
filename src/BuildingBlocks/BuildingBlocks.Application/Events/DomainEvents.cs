using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using TreasuryKit.BuildingBlocks.Domain;

namespace TreasuryKit.BuildingBlocks.Application.Events;

public interface IDomainEventHandler<in TEvent>
    where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken);
}

public interface IDomainEventDispatcher
{
    Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken);
}

public sealed class DomainEventDispatcher(IServiceProvider services) : IDomainEventDispatcher
{
    private static readonly ConcurrentDictionary<Type, Wrapper> Wrappers = new();

    public async Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken)
    {
        foreach (var domainEvent in domainEvents)
        {
            var wrapper = Wrappers.GetOrAdd(
                domainEvent.GetType(),
                static t => (Wrapper)Activator.CreateInstance(typeof(Wrapper<>).MakeGenericType(t))!);
            await wrapper.DispatchAsync(domainEvent, services, cancellationToken);
        }
    }

    private abstract class Wrapper
    {
        public abstract Task DispatchAsync(IDomainEvent domainEvent, IServiceProvider sp, CancellationToken ct);
    }

    private sealed class Wrapper<TEvent> : Wrapper
        where TEvent : IDomainEvent
    {
        public override async Task DispatchAsync(IDomainEvent domainEvent, IServiceProvider sp, CancellationToken ct)
        {
            foreach (var handler in sp.GetServices<IDomainEventHandler<TEvent>>())
            {
                await handler.HandleAsync((TEvent)domainEvent, ct);
            }
        }
    }
}
