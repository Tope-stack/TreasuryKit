using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using TreasuryKit.BuildingBlocks.Infrastructure.Persistence;
using TreasuryKit.Core.Application;
using TreasuryKit.Core.Domain.Tenants;

namespace TreasuryKit.Core.Infrastructure.Persistence;

public sealed class CoreDbContext(DbContextOptions<CoreDbContext> options, ModuleDbContextDependencies dependencies)
    : ModuleDbContext(options, dependencies), ICoreDbContext
{
    public const string SchemaName = "core";

    public override string Schema => SchemaName;

    public DbSet<Tenant> Tenants => Set<Tenant>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(b =>
        {
            b.ToTable("tenants");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Name).HasMaxLength(Tenant.NameMaxLength);
            b.Property(x => x.BusinessTimeZone).HasMaxLength(64);
        });
    }
}

/// <summary>Used by `dotnet ef` only.</summary>
internal sealed class CoreDbContextDesignTimeFactory : IDesignTimeDbContextFactory<CoreDbContext>
{
    public CoreDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CoreDbContext>();
        PersistenceExtensions.ConfigureNpgsql(options, "Host=localhost;Database=treasurykit_design", CoreDbContext.SchemaName);
        return new CoreDbContext(options.Options, DesignTimeDependencies.Create());
    }
}
