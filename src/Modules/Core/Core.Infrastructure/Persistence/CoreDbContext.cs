using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TreasuryKit.BuildingBlocks.Infrastructure.Persistence;
using TreasuryKit.Core.Application;
using TreasuryKit.Core.Domain.Accounts;
using TreasuryKit.Core.Domain.Counterparties;
using TreasuryKit.Core.Domain.MappingProfiles;
using TreasuryKit.Core.Domain.Statements;
using TreasuryKit.Core.Domain.Tenants;

namespace TreasuryKit.Core.Infrastructure.Persistence;

public sealed class CoreDbContext(DbContextOptions<CoreDbContext> options, ModuleDbContextDependencies dependencies)
    : ModuleDbContext(options, dependencies), ICoreDbContext
{
    public const string SchemaName = "core";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public override string Schema => SchemaName;

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();

    public DbSet<BankMappingProfile> MappingProfiles => Set<BankMappingProfile>();

    public DbSet<Counterparty> Counterparties => Set<Counterparty>();

    public DbSet<StatementImport> StatementImports => Set<StatementImport>();

    public DbSet<StatementLine> StatementLines => Set<StatementLine>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(b =>
        {
            b.ToTable("tenants");
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Name).HasMaxLength(Tenant.NameMaxLength);
            b.Property(x => x.BusinessTimeZone).HasMaxLength(64);
            b.Property(x => x.InvoiceReferencePatterns).HasDefaultValueSql(@"ARRAY['INV[-/ ]?\d+']::text[]");
        });

        modelBuilder.Entity<BankAccount>(b =>
        {
            b.ToTable("bank_accounts");
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.BankCode).HasMaxLength(20);
            b.Property(x => x.BankName).HasMaxLength(100);
            b.Property(x => x.AccountNumber).HasMaxLength(34);
            b.Property(x => x.DisplayName).HasMaxLength(200);
            b.HasIndex(x => new { x.TenantId, x.BankCode, x.AccountNumber, x.Currency }).IsUnique();
        });

        modelBuilder.Entity<Counterparty>(b =>
        {
            b.ToTable("counterparties");
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Name).HasMaxLength(200);
            b.Property(x => x.NormalizedName).HasMaxLength(200);
            b.Property(x => x.Country).HasMaxLength(2);
            b.HasIndex(x => new { x.TenantId, x.NormalizedName }).IsUnique();
        });

        modelBuilder.Entity<BankMappingProfile>(b =>
        {
            b.ToTable("mapping_profiles");
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.BankCode).HasMaxLength(20);
            b.Property(x => x.Name).HasMaxLength(200);
            b.Property(x => x.FileType).HasConversion<string>().HasMaxLength(10);
            b.Property(x => x.AmountMode).HasConversion<string>().HasMaxLength(30);
            b.Property(x => x.Delimiter).HasMaxLength(5);
            b.Property(x => x.Culture).HasMaxLength(20);
            b.Property(x => x.ColumnMap).HasColumnType("jsonb").HasConversion(JsonConverter<ColumnMap>());
            b.Property(x => x.OpeningBalanceLocator).HasColumnType("jsonb").HasConversion(JsonConverter<BalanceLocator?>());
            b.Property(x => x.ClosingBalanceLocator).HasColumnType("jsonb").HasConversion(JsonConverter<BalanceLocator?>());
            b.Ignore(x => x.IsSystem);
            b.HasIndex(x => x.TenantId);

            // System-wide profiles (no tenant) are visible to every tenant (ADR 0004).
            b.HasQueryFilter(x => x.TenantId == null || x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<StatementImport>(b =>
        {
            b.ToTable("statement_imports");
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.FileName).HasMaxLength(260);
            b.Property(x => x.FileHash).HasMaxLength(64).IsFixedLength();
            b.Property(x => x.FileKey).HasMaxLength(500);
            b.Property(x => x.Format).HasConversion<string>().HasMaxLength(10);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Error).HasMaxLength(2000);
            b.Property(x => x.ImportedBy).HasMaxLength(200);
            b.Property(x => x.AcknowledgedBy).HasMaxLength(200);
            b.Property(x => x.SkippedEntryCount).HasDefaultValue(0);
            b.HasIndex(x => new { x.TenantId, x.AccountId, x.FileHash }).IsUnique();
            b.HasIndex(x => new { x.AccountId, x.ImportedAt });
        });

        modelBuilder.Entity<StatementLine>(b =>
        {
            b.ToTable("statement_lines");
            b.Property(x => x.Id).ValueGeneratedNever();
            b.ComplexProperty(x => x.Amount, money =>
            {
                money.Property(m => m.Amount).HasColumnName("amount");
                money.Property(m => m.Currency).HasColumnName("currency");
            });
            b.Property(x => x.BankReference).HasMaxLength(100);
            b.Property(x => x.ExtractedRefs).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
            b.Property(x => x.CounterpartyNameGuess).HasMaxLength(200);
            b.Property(x => x.EndToEndId).HasMaxLength(35);
            b.Property(x => x.CounterpartyName).HasMaxLength(200);
            b.Property(x => x.Fingerprint).HasMaxLength(64).IsFixedLength();
            b.HasIndex(x => new { x.TenantId, x.AccountId, x.Fingerprint }).IsUnique();
            b.HasIndex(x => new { x.AccountId, x.ValueDate });
            b.HasIndex(x => x.ImportId);
            b.HasIndex(x => x.ExtractedRefs).HasMethod("gin");
            // (account_id, amount) index is created in the migration: EF cannot index complex-type columns.
        });
    }

    private static ValueConverter<T, string> JsonConverter<T>() =>
        new(v => JsonSerializer.Serialize(v, Json), s => JsonSerializer.Deserialize<T>(s, Json)!);
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
