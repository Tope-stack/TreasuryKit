using Microsoft.Extensions.Logging;
using TreasuryKit.BuildingBlocks.Application.Events;
using TreasuryKit.Core.Contracts.Statements;
using TreasuryKit.Core.Contracts.Tenants;
using TreasuryKit.Recon.Application.Configuration;
using TreasuryKit.Recon.Application.Runs;
using TreasuryKit.Recon.Domain.Matches;
using TreasuryKit.Recon.Domain.Runs;

namespace TreasuryKit.Recon.Application.Events;

/// <summary>Seeds default matching rules and thresholds for a new tenant.</summary>
public sealed class TenantCreatedHandler(IReconDbContext db, ReconDefaults defaults) : IIntegrationEventHandler<TenantCreated>
{
    public async Task HandleAsync(TenantCreated integrationEvent, CancellationToken cancellationToken)
    {
        await defaults.EnsureAsync(integrationEvent.TenantId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// New statement lines trigger an automatic run for the import's period (plan §5.4). If a run is already active, it
/// records a follow-up so the new lines are matched when it finishes.
/// </summary>
public sealed class StatementImportedHandler(IReconDbContext db, RunQueue queue, ILogger<StatementImportedHandler> logger)
    : IIntegrationEventHandler<StatementImported>
{
    public async Task HandleAsync(StatementImported integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent is not { PeriodStart: { } from, PeriodEnd: { } to } || integrationEvent.NewLineCount == 0)
        {
            return;
        }

        if (await queue.FindActiveAsync(integrationEvent.AccountId, cancellationToken) is { } active)
        {
            active.RequestRerun(from, to);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var queued = await queue.QueueAsync(
            integrationEvent.TenantId, integrationEvent.AccountId, from, to, RunTrigger.Event, Match.SystemUser, cancellationToken);
        if (queued.IsFailure)
        {
            if (queued.Error.Code == "Run.AlreadyActive" && await queue.FindActiveAsync(integrationEvent.AccountId, cancellationToken) is { } winner)
            {
                winner.RequestRerun(from, to);
                await db.SaveChangesAsync(cancellationToken);
                return;
            }

            logger.LogInformation(
                "No automatic run for import {ImportId}: {Reason}", integrationEvent.ImportId, queued.Error.Message);
        }
    }
}
