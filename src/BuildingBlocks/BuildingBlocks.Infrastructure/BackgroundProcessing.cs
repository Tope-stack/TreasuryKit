using Microsoft.Extensions.DependencyInjection;
using TreasuryKit.BuildingBlocks.Infrastructure.Jobs;
using TreasuryKit.BuildingBlocks.Infrastructure.Outbox;

namespace TreasuryKit.BuildingBlocks.Infrastructure;

public static class BackgroundProcessing
{
    /// <summary>Setting that makes the Api run background work itself, for hosts without a separate Worker (ADR 0011).</summary>
    public const string RunInApiSetting = "Hosting:RunBackgroundJobs";

    /// <summary>The outbox dispatcher and the job runner. Hosted by the Worker, or by the Api when <see cref="RunInApiSetting"/> is on.</summary>
    public static IServiceCollection AddBackgroundProcessing(this IServiceCollection services)
    {
        services.AddHostedService<OutboxBackgroundService>();
        services.AddHostedService<JobBackgroundService>();
        return services;
    }
}
