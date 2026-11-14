using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TreasuryKit.BuildingBlocks.Application.Abstractions;
using TreasuryKit.BuildingBlocks.Application.Events;
using TreasuryKit.BuildingBlocks.Application.Jobs;
using TreasuryKit.BuildingBlocks.Application.Messaging;
using TreasuryKit.BuildingBlocks.Infrastructure.Auditing;
using TreasuryKit.BuildingBlocks.Infrastructure.Context;
using TreasuryKit.BuildingBlocks.Infrastructure.Files;
using TreasuryKit.BuildingBlocks.Infrastructure.Inbox;
using TreasuryKit.BuildingBlocks.Infrastructure.Jobs;
using TreasuryKit.BuildingBlocks.Infrastructure.Outbox;
using TreasuryKit.BuildingBlocks.Infrastructure.Persistence;

namespace TreasuryKit.BuildingBlocks.Infrastructure;

public static class BuildingBlocksExtensions
{
    /// <summary>Registers services shared by every host and module.</summary>
    public static IServiceCollection AddBuildingBlocks(this IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<TenantContext>();
        services.AddScoped<ICurrentTenant>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<UserContext>();
        services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<UserContext>());

        services.AddScoped<IDispatcher, Dispatcher>();
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.AddScoped<ModuleDbContextDependencies>();
        services.AddScoped<AuditSaveChangesInterceptor>();

        services.AddSingleton<IEventBus, InProcessEventBus>();
        services.AddSingleton<OutboxDispatcher>();
        services.AddSingleton<JobExecutor>();
        services.AddSingleton<JobDispatcher>();
        services.AddSingleton<LocalFileStore>();
        services.AddSingleton<DatabaseFileStore>();
        services.AddSingleton<IFileStore>(sp => sp.GetRequiredService<IOptions<FileStoreOptions>>().Value.Provider == FileStoreProvider.Database
            ? sp.GetRequiredService<DatabaseFileStore>()
            : sp.GetRequiredService<LocalFileStore>());

        services.AddOptions<FileStoreOptions>()
            .BindConfiguration(FileStoreOptions.SectionName)
            .Validate(o => o.Provider != FileStoreProvider.Local || !string.IsNullOrWhiteSpace(o.RootPath), "FileStore:RootPath is required for the Local provider.")
            .ValidateOnStart();

        services.AddOptions<JobOptions>()
            .BindConfiguration(JobOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(o => o.PollingInterval > TimeSpan.Zero && o.Lease > TimeSpan.Zero, "Jobs:PollingInterval and Jobs:Lease must be positive.")
            .ValidateOnStart();

        services.AddOptions<DatabaseOptions>()
            .BindConfiguration("Database")
            .Configure<IConfiguration>((o, config) =>
                o.ConnectionString = ConnectionStrings.Normalize(config.GetConnectionString(DatabaseOptions.ConnectionStringName)))
            .Validate(o => !string.IsNullOrWhiteSpace(o.ConnectionString), $"ConnectionStrings:{DatabaseOptions.ConnectionStringName} is required.")
            .ValidateOnStart();

        services.AddOptions<OutboxOptions>()
            .BindConfiguration(OutboxOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(o => o.PollingInterval > TimeSpan.Zero, "Outbox:PollingInterval must be positive.")
            .ValidateOnStart();

        return services;
    }

    /// <summary>
    /// Subscribes <typeparamref name="THandler"/> to <typeparamref name="TEvent"/>, de-duplicated through
    /// the inbox of the subscribing module's <typeparamref name="TContext"/>.
    /// </summary>
    public static IServiceCollection AddIntegrationEventHandler<TEvent, THandler, TContext>(this IServiceCollection services)
        where TEvent : IIntegrationEvent
        where THandler : class, IIntegrationEventHandler<TEvent>
        where TContext : ModuleDbContext
    {
        services.AddScoped<THandler>();
        services.AddScoped<InboxInvoker<TEvent, THandler, TContext>>();
        services.AddSingleton(new IntegrationEventSubscription(typeof(TEvent), typeof(InboxInvoker<TEvent, THandler, TContext>)));
        return services;
    }

    /// <summary>Registers <typeparamref name="THandler"/> to run <typeparamref name="TJob"/> jobs.</summary>
    public static IServiceCollection AddJobHandler<TJob, THandler>(this IServiceCollection services)
        where TJob : IJob
        where THandler : class, IJobHandler<TJob>
    {
        services.AddScoped<THandler>();
        services.AddScoped<JobInvoker<TJob, THandler>>();
        services.AddSingleton(new JobHandlerRegistration(typeof(TJob), typeof(JobInvoker<TJob, THandler>)));
        return services;
    }
}
