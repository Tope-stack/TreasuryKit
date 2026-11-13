using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using TreasuryKit.BuildingBlocks.Infrastructure.Persistence;
using TreasuryKit.Recon.Application;
using TreasuryKit.Recon.Domain.Configuration;
using TreasuryKit.Recon.Domain.Exceptions;
using TreasuryKit.Recon.Domain.Matches;
using TreasuryKit.Recon.Domain.Matching;
using TreasuryKit.Recon.Domain.Runs;

namespace TreasuryKit.Recon.Infrastructure.Persistence;

public sealed class ReconDbContext(DbContextOptions<ReconDbContext> options, ModuleDbContextDependencies dependencies)
    : ModuleDbContext(options, dependencies), IReconDbContext
{
    public const string SchemaName = "recon";

    public override string Schema => SchemaName;

    public DbSet<ReconciliationRun> Runs => Set<ReconciliationRun>();

    public DbSet<Match> Matches => Set<Match>();

    public DbSet<MatchItem> MatchItems => Set<MatchItem>();

    public DbSet<ReconciledItem> ReconciledItems => Set<ReconciledItem>();

    public DbSet<ReconException> Exceptions => Set<ReconException>();

    public DbSet<MatchingRuleConfig> RuleConfigs => Set<MatchingRuleConfig>();

    public DbSet<ReconSettings> Settings => Set<ReconSettings>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ReconciliationRun>(b =>
        {
            b.ToTable("reconciliation_runs");
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.TriggeredBy).HasConversion<string>().HasMaxLength(10);
            b.Property(x => x.RequestedBy).HasMaxLength(200);
            b.Property(x => x.Error).HasMaxLength(2000);
            b.Property(x => x.Stats).HasJsonConversion();
            b.HasIndex(x => new { x.AccountId, x.CreatedAt });

            // One active run per account (plan §3 Concurrency). Queued is included so racing requests can't both queue.
            b.HasIndex(x => new { x.TenantId, x.AccountId })
                .IsUnique()
                .HasFilter("status IN ('Queued', 'Running')")
                .HasDatabaseName("ux_reconciliation_runs_one_active_per_account");
        });

        modelBuilder.Entity<Match>(b =>
        {
            b.ToTable("matches");
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.RuleKey).HasMaxLength(50);
            b.Property(x => x.ScoreBreakdown).HasJsonConversion();
            b.Property(x => x.ProposedBy).HasMaxLength(200);
            b.Property(x => x.ConfirmedBy).HasMaxLength(200);
            b.Property(x => x.Reason).HasMaxLength(500);
            b.Property(x => x.ReviewedBy).HasMaxLength(200);
            b.Ignore(x => x.IsManual);
            b.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.MatchId);
            b.Navigation(x => x.Items).HasField("_items").UsePropertyAccessMode(PropertyAccessMode.Field);
            b.HasIndex(x => new { x.AccountId, x.Status, x.ValueDate });
            b.HasIndex(x => x.RunId);
        });

        modelBuilder.Entity<MatchItem>(b =>
        {
            b.ToTable("match_items");
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.ItemType).HasConversion<string>().HasMaxLength(30);
            b.ComplexProperty(x => x.AllocatedAmount, money =>
            {
                money.Property(m => m.Amount).HasColumnName("allocated_amount");
                money.Property(m => m.Currency).HasColumnName("currency");
            });
            b.HasIndex(x => new { x.ItemType, x.ItemId });
        });

        modelBuilder.Entity<ReconciledItem>(b =>
        {
            b.ToTable("reconciled_items");
            b.HasKey(x => new { x.TenantId, x.ItemType, x.ItemId });
            b.Property(x => x.ItemType).HasConversion<string>().HasMaxLength(30);
            b.HasIndex(x => x.AccountId);
            b.HasIndex(x => x.MatchId);
        });

        modelBuilder.Entity<ReconException>(b =>
        {
            b.ToTable("exceptions");
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.ItemType).HasConversion<string>().HasMaxLength(30);
            b.Property(x => x.Reason).HasConversion<string>().HasMaxLength(30);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Candidates).HasJsonConversion();
            b.Property(x => x.ResolvedBy).HasMaxLength(200);
            b.Property(x => x.ResolutionNote).HasMaxLength(1000);
            b.HasIndex(x => new { x.AccountId, x.Status });
            b.HasIndex(x => new { x.ItemType, x.ItemId });
        });

        modelBuilder.Entity<MatchingRuleConfig>(b =>
        {
            b.ToTable("matching_rule_configs");
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.RuleKey).HasMaxLength(50);
            b.Property(x => x.Parameters).HasJsonConversion();
            b.HasIndex(x => new { x.TenantId, x.RuleKey }).IsUnique();
        });

        modelBuilder.Entity<ReconSettings>(b =>
        {
            b.ToTable("settings");
            b.HasKey(x => x.TenantId);
            b.Property(x => x.AutoConfirmThreshold).HasPrecision(5, 4);
            b.Property(x => x.ProposeThreshold).HasPrecision(5, 4);
            b.Ignore(x => x.Thresholds);
        });
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
