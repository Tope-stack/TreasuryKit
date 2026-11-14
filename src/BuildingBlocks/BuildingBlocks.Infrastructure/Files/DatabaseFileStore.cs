using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TreasuryKit.BuildingBlocks.Application.Abstractions;
using TreasuryKit.BuildingBlocks.Infrastructure.Idempotency;

namespace TreasuryKit.BuildingBlocks.Infrastructure.Files;

/// <summary>A stored upload. Keys already start with the tenant id (e.g. "{tenant}/statements/{sha256}.csv").</summary>
public sealed class StoredFile
{
    public string Key { get; set; } = null!;

    public byte[] Content { get; set; } = null!;

    public long Size { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Keeps uploads in PostgreSQL, for hosts whose filesystem is not persistent or not shared (ADR 0011).
/// Statement files are capped at 20 MB, so holding one in memory is fine.
/// </summary>
public sealed class DatabaseFileStore(IServiceScopeFactory scopes, IClock clock) : IFileStore
{
    public async Task SaveAsync(string key, Stream content, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var file = await db.StoredFiles.FirstOrDefaultAsync(f => f.Key == key, cancellationToken);
        if (file is null)
        {
            file = new StoredFile { Key = key };
            db.StoredFiles.Add(file);
        }

        file.Content = buffer.ToArray();
        file.Size = file.Content.LongLength;
        file.CreatedAt = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var content = await db.StoredFiles.AsNoTracking().Where(f => f.Key == key).Select(f => f.Content).FirstOrDefaultAsync(cancellationToken);
        return content is null
            ? throw new FileNotFoundException($"Stored file '{key}' not found.")
            : new MemoryStream(content, writable: false);
    }
}
