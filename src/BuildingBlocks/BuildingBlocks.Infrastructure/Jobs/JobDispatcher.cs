using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TreasuryKit.BuildingBlocks.Infrastructure.Jobs;

public sealed class JobOptions
{
    public const string SectionName = "Jobs";

    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>How long a claimed job is reserved for one worker before others may retry it.</summary>
    public TimeSpan Lease { get; set; } = TimeSpan.FromMinutes(10);

    [Range(1, 100)]
    public int BatchSize { get; set; } = 5;
}

/// <summary>Runs due jobs from every module once. Hosted by the Worker; called directly by tests.</summary>
public sealed class JobDispatcher(IServiceScopeFactory scopes, ILogger<JobDispatcher> logger)
{
    public async Task<int> RunDueJobsAsync(CancellationToken cancellationToken)
    {
        var claimed = 0;
        await using var scope = scopes.CreateAsyncScope();
        foreach (var store in scope.ServiceProvider.GetServices<IJobStore>())
        {
            try
            {
                claimed += await store.RunDueJobsAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Running jobs from {Schema} failed", store.Schema);
            }
        }

        return claimed;
    }
}

public sealed class JobBackgroundService(
    JobDispatcher dispatcher,
    IOptions<JobOptions> options,
    ILogger<JobBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Job runner started, polling every {Interval}", options.Value.PollingInterval);
        using var timer = new PeriodicTimer(options.Value.PollingInterval);
        do
        {
            while (await dispatcher.RunDueJobsAsync(stoppingToken) > 0)
            {
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
