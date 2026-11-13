using TreasuryKit.BuildingBlocks.Domain;

namespace TreasuryKit.BuildingBlocks.Application.Messaging;

/// <summary>Routes commands and queries to their single registered handler.</summary>
public interface IDispatcher
{
    Task<Result> SendAsync(ICommand command, CancellationToken cancellationToken = default);

    Task<Result<TResponse>> SendAsync<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default);

    Task<Result<TResponse>> QueryAsync<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default);
}
