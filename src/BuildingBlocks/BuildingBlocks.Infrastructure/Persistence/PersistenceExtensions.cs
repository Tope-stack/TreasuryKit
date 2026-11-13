using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TreasuryKit.BuildingBlocks.Infrastructure.Auditing;
using TreasuryKit.BuildingBlocks.Infrastructure.Jobs;
using TreasuryKit.BuildingBlocks.Infrastructure.Outbox;

namespace TreasuryKit.BuildingBlocks.Infrastructure.Persistence;

public static class PersistenceExtensions
{
    /// <summary>Registers a module DbContext, its outbox and job stores, and its health check.</summary>
    public static IServiceCollection AddModuleDbContext<TContext>(this IServiceCollection services, string schema)
        where TContext : ModuleDbContext
    {
        services.AddDbContext<TContext>((sp, options) =>
            ConfigureNpgsql(options, sp.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString, schema)
                .AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>()));

        services.AddSingleton(new ModuleDbContextRegistration(typeof(TContext), schema));
        services.AddScoped<IOutboxStore, OutboxStore<TContext>>();
        services.AddScoped<IJobStore, JobStore<TContext>>();
        services.AddHealthChecks().AddDbContextCheck<TContext>($"db-{schema}");
        return services;
    }

    /// <summary>Shared Npgsql configuration; also used by design-time factories.</summary>
    public static DbContextOptionsBuilder ConfigureNpgsql(DbContextOptionsBuilder options, string connectionString, string schema) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", schema))
            .UseSnakeCaseNamingConvention();

    /// <summary>Applies pending migrations for every registered module, in registration order.</summary>
    public static async Task MigrateModuleDatabasesAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        foreach (var registration in scope.ServiceProvider.GetServices<ModuleDbContextRegistration>())
        {
            var context = (DbContext)scope.ServiceProvider.GetRequiredService(registration.ContextType);
            await context.Database.MigrateAsync(cancellationToken);
        }
    }
}
