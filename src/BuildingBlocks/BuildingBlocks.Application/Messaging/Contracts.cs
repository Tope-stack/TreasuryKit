using TreasuryKit.BuildingBlocks.Domain;

namespace TreasuryKit.BuildingBlocks.Application.Messaging;

/// <summary>A command that changes state and returns no value.</summary>
public interface ICommand;

/// <summary>A command that changes state and returns <typeparamref name="TResponse"/>.</summary>
public interface ICommand<TResponse>;

/// <summary>A read-only request.</summary>
public interface IQuery<TResponse>;

public interface ICommandHandler<in TCommand>
    where TCommand : ICommand
{
    Task<Result> HandleAsync(TCommand command, CancellationToken cancellationToken);
}

public interface ICommandHandler<in TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    Task<Result<TResponse>> HandleAsync(TCommand command, CancellationToken cancellationToken);
}

public interface IQueryHandler<in TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    Task<Result<TResponse>> HandleAsync(TQuery query, CancellationToken cancellationToken);
}
