using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TreasuryKit.BuildingBlocks.Application.Abstractions;
using TreasuryKit.BuildingBlocks.Application.Events;
using TreasuryKit.Recon.Contracts.Matches;

namespace TreasuryKit.Core.Application.ExpectedTransactions;

/// <summary>Settles expected transactions covered by a confirmed match (plan §4.5). Idempotent through the inbox.</summary>
public sealed class MatchConfirmedHandler(ICoreDbContext db, IClock clock, ILogger<MatchConfirmedHandler> logger)
    : IIntegrationEventHandler<MatchConfirmed>
{
    public async Task HandleAsync(MatchConfirmed integrationEvent, CancellationToken cancellationToken)
    {
        foreach (var item in integrationEvent.Items.Where(i => i.Type == MatchedItemType.ExpectedTransaction))
        {
            var transaction = await db.ExpectedTransactions.FirstOrDefaultAsync(t => t.Id == item.ItemId, cancellationToken);
            if (transaction is null)
            {
                logger.LogWarning("Match {MatchId} confirmed unknown expected transaction {Id}", integrationEvent.MatchId, item.ItemId);
                continue;
            }

            var applied = transaction.ApplyAllocation(item.AllocatedAmount, clock.UtcNow);
            if (applied != item.AllocatedAmount)
            {
                logger.LogWarning(
                    "Match {MatchId} allocated {Allocated} to {Id} but only {Applied} was outstanding",
                    integrationEvent.MatchId, item.AllocatedAmount, item.ItemId, applied);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Restores the outstanding amount when a confirmed match is reversed.</summary>
public sealed class MatchReversedHandler(ICoreDbContext db, IClock clock) : IIntegrationEventHandler<MatchReversed>
{
    public async Task HandleAsync(MatchReversed integrationEvent, CancellationToken cancellationToken)
    {
        foreach (var item in integrationEvent.Items.Where(i => i.Type == MatchedItemType.ExpectedTransaction))
        {
            var transaction = await db.ExpectedTransactions.FirstOrDefaultAsync(t => t.Id == item.ItemId, cancellationToken);
            transaction?.ReverseAllocation(item.AllocatedAmount, clock.UtcNow);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
