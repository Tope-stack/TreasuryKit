using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TreasuryKit.BuildingBlocks.Infrastructure.Outbox;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(2);

    [Range(1, 1000)]
    public int BatchSize { get; set; } = 50;

    /// <summary>After this many failed attempts a message is parked (left unprocessed with its last error).</summary>
    [Range(1, 100)]
    public int MaxAttempts { get; set; } = 10;
}

/// <summary>Drains every module's outbox once. Hosted by the Worker; called directly by tests.</summary>
public sealed class OutboxDispatcher(IServiceScopeFactory scopes, ILogger<OutboxDispatcher> logger)
{
    public async Task<int> DispatchPendingAsync(CancellationToken cancellationToken)
    {
        var delivered = 0;
        await using var scope = scopes.CreateAsyncScope();
        foreach (var store in scope.ServiceProvider.GetServices<IOutboxStore>())
        {
            try
            {
                delivered += await store.ProcessBatchAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Processing the {Schema} outbox failed", store.Schema);
            }
        }

        return delivered;
    }
}

public sealed class OutboxBackgroundService(
    OutboxDispatcher dispatcher,
    IOptions<OutboxOptions> options,
    ILogger<OutboxBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Outbox dispatcher started, polling every {Interval}", options.Value.PollingInterval);
        using var timer = new PeriodicTimer(options.Value.PollingInterval);
        do
        {
            // Keep draining while messages are flowing; otherwise wait for the next tick.
            while (await dispatcher.DispatchPendingAsync(stoppingToken) > 0)
            {
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
