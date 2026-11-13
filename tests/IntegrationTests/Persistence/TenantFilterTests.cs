using Microsoft.EntityFrameworkCore;
using Npgsql;
using TreasuryKit.BuildingBlocks.Domain;
using TreasuryKit.BuildingBlocks.Infrastructure.Persistence;
using TreasuryKit.IntegrationTests.Infrastructure;

namespace TreasuryKit.IntegrationTests.Persistence;

/// <summary>Verifies the ModuleDbContext tenant query filter on a throwaway schema in its own database.</summary>
[Collection(ApiCollection.Name)]
public sealed class TenantFilterTests(ApiFactory factory) : IAsyncLifetime
{
    private static readonly Guid TenantA = Guid.CreateVersion7();
    private static readonly Guid TenantB = Guid.CreateVersion7();

    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await using var db = CreateContext(tenantId: null);
        await db.Database.EnsureDeletedAsync(_ct);
        await db.Database.EnsureCreatedAsync(_ct);
        db.Widgets.AddRange(new Widget(TenantA, "a1"), new Widget(TenantA, "a2"), new Widget(TenantB, "b1"));
        await db.SaveChangesAsync(_ct);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Queries_only_see_the_current_tenants_rows()
    {
        await using var db = CreateContext(TenantA);

        var names = await db.Widgets.Select(w => w.Name).OrderBy(n => n).ToListAsync(_ct);

        Assert.Equal(["a1", "a2"], names);
    }

    [Fact]
    public async Task Without_a_tenant_no_rows_are_visible()
    {
        await using var db = CreateContext(tenantId: null);

        Assert.Empty(await db.Widgets.ToListAsync(_ct));
    }

    [Fact]
    public async Task Filter_can_be_bypassed_explicitly()
    {
        await using var db = CreateContext(TenantA);

        Assert.Equal(3, await db.Widgets.IgnoreQueryFilters().CountAsync(_ct));
    }

    private FilterTestDbContext CreateContext(Guid? tenantId)
    {
        var connectionString = new NpgsqlConnectionStringBuilder(factory.ConnectionString) { Database = "tenant_filter_test" }.ConnectionString;
        var options = new DbContextOptionsBuilder<FilterTestDbContext>();
        PersistenceExtensions.ConfigureNpgsql(options, connectionString, FilterTestDbContext.SchemaName);
        return new FilterTestDbContext(options.Options, DesignTimeDependencies.Create(tenantId));
    }

    private sealed class Widget(Guid tenantId, string name) : ITenantOwned
    {
        public Guid Id { get; private set; } = Guid.CreateVersion7();

        public Guid TenantId { get; private set; } = tenantId;

        public string Name { get; private set; } = name;
    }

    private sealed class FilterTestDbContext(DbContextOptions<FilterTestDbContext> options, ModuleDbContextDependencies dependencies)
        : ModuleDbContext(options, dependencies)
    {
        public const string SchemaName = "filter_test";

        public override string Schema => SchemaName;

        public DbSet<Widget> Widgets => Set<Widget>();

        protected override void ConfigureModel(ModelBuilder modelBuilder) => modelBuilder.Entity<Widget>().ToTable("widgets");
    }
}
