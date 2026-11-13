using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using TreasuryKit.BuildingBlocks.Domain;

namespace TreasuryKit.BuildingBlocks.Application.Messaging;

/// <summary>
/// Reflection-free at call time: a typed wrapper per message type is built once and cached.
/// </summary>
public sealed class Dispatcher(IServiceProvider services) : IDispatcher
{
    private static readonly ConcurrentDictionary<Type, object> Wrappers = new();

    public Task<Result> SendAsync(ICommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var wrapper = (CommandWrapper)Wrappers.GetOrAdd(
            command.GetType(),
            static t => Activator.CreateInstance(typeof(VoidCommandWrapper<>).MakeGenericType(t))!);
        return wrapper.HandleAsync(command, services, cancellationToken);
    }

    public Task<Result<TResponse>> SendAsync<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var wrapper = (CommandWrapper<TResponse>)Wrappers.GetOrAdd(
            command.GetType(),
            static t => Activator.CreateInstance(typeof(CommandWrapper<,>).MakeGenericType(t, typeof(TResponse)))!);
        return wrapper.HandleAsync(command, services, cancellationToken);
    }

    public Task<Result<TResponse>> QueryAsync<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var wrapper = (QueryWrapper<TResponse>)Wrappers.GetOrAdd(
            query.GetType(),
            static t => Activator.CreateInstance(typeof(QueryWrapper<,>).MakeGenericType(t, typeof(TResponse)))!);
        return wrapper.HandleAsync(query, services, cancellationToken);
    }

    private abstract class CommandWrapper
    {
        public abstract Task<Result> HandleAsync(ICommand command, IServiceProvider sp, CancellationToken ct);
    }

    private sealed class VoidCommandWrapper<TCommand> : CommandWrapper
        where TCommand : ICommand
    {
        public override Task<Result> HandleAsync(ICommand command, IServiceProvider sp, CancellationToken ct) =>
            sp.GetRequiredService<ICommandHandler<TCommand>>().HandleAsync((TCommand)command, ct);
    }

    private abstract class CommandWrapper<TResponse>
    {
        public abstract Task<Result<TResponse>> HandleAsync(ICommand<TResponse> command, IServiceProvider sp, CancellationToken ct);
    }

    private sealed class CommandWrapper<TCommand, TResponse> : CommandWrapper<TResponse>
        where TCommand : ICommand<TResponse>
    {
        public override Task<Result<TResponse>> HandleAsync(ICommand<TResponse> command, IServiceProvider sp, CancellationToken ct) =>
            sp.GetRequiredService<ICommandHandler<TCommand, TResponse>>().HandleAsync((TCommand)command, ct);
    }

    private abstract class QueryWrapper<TResponse>
    {
        public abstract Task<Result<TResponse>> HandleAsync(IQuery<TResponse> query, IServiceProvider sp, CancellationToken ct);
    }

    private sealed class QueryWrapper<TQuery, TResponse> : QueryWrapper<TResponse>
        where TQuery : IQuery<TResponse>
    {
        public override Task<Result<TResponse>> HandleAsync(IQuery<TResponse> query, IServiceProvider sp, CancellationToken ct) =>
            sp.GetRequiredService<IQueryHandler<TQuery, TResponse>>().HandleAsync((TQuery)query, ct);
    }
}
