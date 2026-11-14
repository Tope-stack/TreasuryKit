using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using TreasuryKit.BuildingBlocks.Infrastructure.Persistence;

namespace TreasuryKit.BuildingBlocks.Infrastructure.Idempotency;

/// <summary>A POST that carried an Idempotency-Key, and the response to replay for repeats.</summary>
public sealed class IdempotencyRecord
{
    public Guid TenantId { get; set; }

    public string Key { get; set; } = null!;

    /// <summary>SHA-256 of method, path, query and body: a reused key with a different request is refused.</summary>
    public string RequestHash { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Null while the original request is still executing.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    public int? StatusCode { get; set; }

    public string? ContentType { get; set; }

    public string? Location { get; set; }

    public byte[]? ResponseBody { get; set; }
}

/// <summary>
/// Host-level data not owned by any module (ADRs 0008, 0011): the <c>platform</c> schema — idempotency records and,
/// when configured, stored files. Deliberately a plain DbContext —
/// no outbox, audit or tenant filter; queries always filter by tenant explicitly.
/// </summary>
public sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options) : DbContext(options)
{
    public const string SchemaName = "platform";

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    public DbSet<Files.StoredFile> StoredFiles => Set<Files.StoredFile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.Entity<IdempotencyRecord>(b =>
        {
            b.ToTable("idempotency_records");
            b.HasKey(x => new { x.TenantId, x.Key });
            b.Property(x => x.Key).HasMaxLength(IdempotencyMiddleware.MaxKeyLength);
            b.Property(x => x.RequestHash).HasMaxLength(64).IsFixedLength();
            b.Property(x => x.ContentType).HasMaxLength(200);
            b.Property(x => x.Location).HasMaxLength(500);
            b.HasIndex(x => x.CreatedAt);
        });

        modelBuilder.Entity<Files.StoredFile>(b =>
        {
            b.ToTable("stored_files");
            b.HasKey(x => x.Key);
            b.Property(x => x.Key).HasMaxLength(500);
        });
    }
}

/// <summary>Used by `dotnet ef` only.</summary>
internal sealed class PlatformDbContextDesignTimeFactory : IDesignTimeDbContextFactory<PlatformDbContext>
{
    public PlatformDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>();
        PersistenceExtensions.ConfigureNpgsql(options, "Host=localhost;Database=treasurykit_design", PlatformDbContext.SchemaName);
        return new PlatformDbContext(options.Options);
    }
}
