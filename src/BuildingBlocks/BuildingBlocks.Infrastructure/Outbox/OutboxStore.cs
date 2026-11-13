using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TreasuryKit.BuildingBlocks.Application.Abstractions;
using TreasuryKit.BuildingBlocks.Application.Events;
using TreasuryKit.BuildingBlocks.Infrastructure.Persistence;

namespace TreasuryKit.BuildingBlocks.Infrastructure.Outbox;

public interface IOutboxStore
{
    string Schema { get; }

    /// <summary>Delivers one batch of pending messages. Returns the number delivered successfully.</summary>
    Task<int> ProcessBatchAsync(CancellationToken cancellationToken);
}

internal sealed class OutboxStore<TContext>(
    TContext db,
    IEventBus bus,
    IClock clock,
    IOptions<OutboxOptions> options,
    ILogger<OutboxStore<TContext>> logger) : IOutboxStore
    where TContext : ModuleDbContext
{
    public string Schema => db.Schema;

    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // SKIP LOCKED lets several workers drain the same outbox without delivering a message twice concurrently.
        // The schema is a compile-time module constant, never user input; values are passed as parameters.
        var sql = $$"""
            SELECT * FROM "{{db.Schema}}".outbox_messages
            WHERE processed_at IS NULL AND attempts < {0}
            ORDER BY occurred_at
            LIMIT {1}
            FOR UPDATE SKIP LOCKED
            """;
        var messages = await db.OutboxMessages
            .FromSqlRaw(sql, settings.MaxAttempts, settings.BatchSize)
            .ToListAsync(cancellationToken);

        var delivered = 0;
        foreach (var message in messages)
        {
            try
            {
                var type = Type.GetType(message.Type)
                    ?? throw new InvalidOperationException($"Unknown integration event type '{message.Type}'.");
                var integrationEvent = (IIntegrationEvent)(JsonSerializer.Deserialize(message.Content, type, IntegrationEventSerializer.Options)
                    ?? throw new InvalidOperationException($"Outbox message {message.Id} has empty content."));

                await bus.PublishAsync(integrationEvent, cancellationToken);
                message.MarkProcessed(clock.UtcNow);
                delivered++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                message.RecordFailure(ex.ToString());
                logger.LogWarning(
                    ex,
                    "Delivering outbox message {MessageId} ({Type}) from {Schema} failed (attempt {Attempt})",
                    message.Id,
                    message.Type,
                    db.Schema,
                    message.Attempts);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return delivered;
    }
}
