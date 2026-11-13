using TreasuryKit.BuildingBlocks.Application.Abstractions;
using TreasuryKit.BuildingBlocks.Application.Messaging;
using TreasuryKit.BuildingBlocks.Domain;
using TreasuryKit.Recon.Domain.Runs;

namespace TreasuryKit.Recon.Application.Runs;

public sealed record StartRunCommand(Guid AccountId, DateOnly From, DateOnly To) : ICommand<Guid>;

internal sealed class StartRunHandler(RunQueue queue, ICurrentTenant tenant, ICurrentUser user) : ICommandHandler<StartRunCommand, Guid>
{
    public Task<Result<Guid>> HandleAsync(StartRunCommand command, CancellationToken cancellationToken) =>
        queue.QueueAsync(
            tenant.TenantId ?? throw new InvalidOperationException("No current tenant."),
            command.AccountId,
            command.From,
            command.To,
            RunTrigger.User,
            user.UserId ?? throw new InvalidOperationException("No current user."),
            cancellationToken);
}
