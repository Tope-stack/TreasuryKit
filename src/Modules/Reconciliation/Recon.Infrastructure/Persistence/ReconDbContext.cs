using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using TreasuryKit.BuildingBlocks.Infrastructure.Persistence;

namespace TreasuryKit.Recon.Infrastructure.Persistence;

public sealed class ReconDbContext(DbContextOptions<ReconDbContext> options, ModuleDbContextDependencies dependencies)
    : ModuleDbContext(options, dependencies)
{
    public const string SchemaName = "recon";

    public override string Schema => SchemaName;

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        // Phase 3: matching rule configs, runs, matches, exceptions.
    }
}

/// <summary>Used by `dotnet ef` only.</summary>
internal sealed class ReconDbContextDesignTimeFactory : IDesignTimeDbContextFactory<ReconDbContext>
{
    public ReconDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ReconDbContext>();
        PersistenceExtensions.ConfigureNpgsql(options, "Host=localhost;Database=treasurykit_design", ReconDbContext.SchemaName);
        return new ReconDbContext(options.Options, DesignTimeDependencies.Create());
    }
}
