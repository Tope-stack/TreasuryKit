using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TreasuryKit.BuildingBlocks.Application.Abstractions;
using TreasuryKit.BuildingBlocks.Application.Events;
using TreasuryKit.BuildingBlocks.Application.Jobs;
using TreasuryKit.BuildingBlocks.Domain;
using TreasuryKit.BuildingBlocks.Infrastructure.Auditing;
using TreasuryKit.BuildingBlocks.Infrastructure.Inbox;
using TreasuryKit.BuildingBlocks.Infrastructure.Jobs;
using TreasuryKit.BuildingBlocks.Infrastructure.Outbox;

namespace TreasuryKit.BuildingBlocks.Infrastructure.Persistence;

/// <summary>Scoped services every module DbContext needs.</summary>
public sealed class ModuleDbContextDependencies(ICurrentTenant tenant, IDomainEventDispatcher domainEvents, IClock clock)
{
    public ICurrentTenant Tenant { get; } = tenant;

    public IClock Clock { get; } = clock;

    public IDomainEventDispatcher DomainEvents { get; } = domainEvents;
}

/// <summary>
/// Base DbContext for a module: owns one PostgreSQL schema, plus that schema's outbox, inbox and audit log.
/// Applies the tenant filter to every <see cref="ITenantOwned"/> entity and xmin concurrency to aggregate roots.
/// </summary>
public abstract class ModuleDbContext(DbContextOptions options, ModuleDbContextDependencies dependencies)
    : DbContext(options), IModuleUnitOfWork
{
    private const int MaxDomainEventRounds = 10;

    public abstract string Schema { get; }

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    public DbSet<AuditEntry> AuditLog => Set<AuditEntry>();

    public DbSet<JobRecord> Jobs => Set<JobRecord>();

    /// <summary>Referenced by tenant query filters; EF evaluates it per context instance. Null matches no rows.</summary>
    public Guid? CurrentTenantId => dependencies.Tenant.TenantId;

    public void Publish(IIntegrationEvent integrationEvent) => OutboxMessages.Add(OutboxMessage.Create(integrationEvent));

    public void Enqueue(IJob job) => Jobs.Add(JobRecord.Create(job, dependencies.Clock.UtcNow));

    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
        new UnitOfWorkTransaction(await Database.BeginTransactionAsync(cancellationToken));

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        await DispatchDomainEventsAsync(cancellationToken);
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        throw new NotSupportedException("Use SaveChangesAsync.");

    protected abstract void ConfigureModel(ModelBuilder modelBuilder);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Money amounts and balances: numeric(19,4). Currencies: ISO 4217 code.
        configurationBuilder.Properties<decimal>().HavePrecision(19, 4);
        configurationBuilder.Properties<Currency>().HaveConversion<CurrencyConverter>().HaveMaxLength(3);
    }

    protected sealed override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        ConfigureBuildingBlocks(modelBuilder);
        ConfigureModel(modelBuilder);
        ApplyConventions(modelBuilder);
    }

    private static void ConfigureBuildingBlocks(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.ToTable("outbox_messages");
            b.HasKey(x => x.Id);
            b.Property(x => x.Type).HasMaxLength(500);
            b.Property(x => x.Content).HasColumnType("jsonb");
            b.HasIndex(x => x.OccurredAt).HasFilter("processed_at IS NULL");
        });

        modelBuilder.Entity<InboxMessage>(b =>
        {
            b.ToTable("inbox_messages");
            b.HasKey(x => new { x.MessageId, x.Handler });
            b.Property(x => x.Handler).HasMaxLength(500);
        });

        modelBuilder.Entity<AuditEntry>(b =>
        {
            b.ToTable("audit_log");
            b.HasKey(x => x.Id);
            b.Property(x => x.UserId).HasMaxLength(200);
            b.Property(x => x.EntityType).HasMaxLength(200);
            b.Property(x => x.EntityId).HasMaxLength(200);
            b.Property(x => x.Action).HasMaxLength(20);
            b.Property(x => x.Before).HasColumnType("jsonb");
            b.Property(x => x.After).HasColumnType("jsonb");
            b.HasIndex(x => new { x.TenantId, x.EntityType, x.EntityId });
        });

        modelBuilder.Entity<JobRecord>(b =>
        {
            b.ToTable("jobs");
            b.HasKey(x => x.Id);
            b.Property(x => x.Type).HasMaxLength(500);
            b.Property(x => x.Payload).HasColumnType("jsonb");
            b.HasIndex(x => x.RunAfter).HasFilter("completed_at IS NULL AND failed_at IS NULL");
        });
    }

    private void ApplyConventions(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes().Where(t => t.BaseType is null).ToList())
        {
            var clrType = entityType.ClrType;

            if (typeof(IHasDomainEvents).IsAssignableFrom(clrType))
            {
                // PostgreSQL system column xmin as optimistic concurrency token.
                modelBuilder.Entity(clrType)
                    .Property<uint>("Version")
                    .HasColumnName("xmin")
                    .HasColumnType("xid")
                    .IsRowVersion();
            }

            if (typeof(ITenantOwned).IsAssignableFrom(clrType))
            {
                modelBuilder.Entity(clrType).HasQueryFilter(BuildTenantFilter(clrType));
            }
        }
    }

    private LambdaExpression BuildTenantFilter(Type clrType)
    {
        // e => (Guid?)e.TenantId == this.CurrentTenantId
        var entity = Expression.Parameter(clrType, "e");
        var tenantId = Expression.Convert(Expression.Property(entity, nameof(ITenantOwned.TenantId)), typeof(Guid?));
        var current = Expression.Property(Expression.Constant(this), nameof(CurrentTenantId));
        return Expression.Lambda(Expression.Equal(tenantId, current), entity);
    }

    private async Task DispatchDomainEventsAsync(CancellationToken cancellationToken)
    {
        for (var round = 0; round < MaxDomainEventRounds; round++)
        {
            var aggregates = ChangeTracker.Entries<IHasDomainEvents>()
                .Select(e => e.Entity)
                .Where(e => e.DomainEvents.Count > 0)
                .ToList();

            if (aggregates.Count == 0)
            {
                return;
            }

            var events = aggregates.SelectMany(a => a.DomainEvents).ToList();
            aggregates.ForEach(a => a.ClearDomainEvents());
            await dependencies.DomainEvents.DispatchAsync(events, cancellationToken);
        }

        throw new InvalidOperationException($"Domain events still pending after {MaxDomainEventRounds} rounds.");
    }

    private sealed class UnitOfWorkTransaction(IDbContextTransaction transaction) : IUnitOfWorkTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken = default) => transaction.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }

    private sealed class CurrencyConverter() : ValueConverter<Currency, string>(c => c.Code, code => Currency.FromCode(code).Value);
}
