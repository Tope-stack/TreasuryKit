using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TreasuryKit.BuildingBlocks.Application.Abstractions;
using TreasuryKit.BuildingBlocks.Domain;

namespace TreasuryKit.BuildingBlocks.Infrastructure.Auditing;

/// <summary>Writes an <see cref="AuditEntry"/> for every added, modified or deleted entity in the same SaveChanges.</summary>
public sealed class AuditSaveChangesInterceptor(ICurrentTenant tenant, ICurrentUser user, IClock clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        AddAuditEntries(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        AddAuditEntries(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void AddAuditEntries(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        context.ChangeTracker.DetectChanges();
        var now = clock.UtcNow;
        var entries = new List<AuditEntry>();

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.Entity is AuditEntry && entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException("The audit log is append-only.");
            }

            if (entry.Entity is INotAudited || entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            var tenantId = entry.Entity is ITenantOwned owned ? owned.TenantId : tenant.TenantId;
            entries.Add(AuditEntry.Create(
                tenantId,
                user.UserId,
                now,
                entry.Metadata.ClrType.Name,
                KeyOf(entry),
                entry.State.ToString(),
                entry.State == EntityState.Added ? null : Serialize(entry, original: true),
                entry.State == EntityState.Deleted ? null : Serialize(entry, original: false)));
        }

        if (entries.Count > 0)
        {
            context.AddRange(entries);
        }
    }

    private static string KeyOf(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        return key is null
            ? string.Empty
            : string.Join(",", key.Properties.Select(p => entry.Property(p.Name).CurrentValue?.ToString()));
    }

    private static string Serialize(EntityEntry entry, bool original)
    {
        var values = new Dictionary<string, object?>();
        foreach (var property in entry.Properties)
        {
            if (property.Metadata.IsConcurrencyToken)
            {
                continue;
            }

            // For updates, only record what changed.
            if (entry.State == EntityState.Modified && !property.IsModified)
            {
                continue;
            }

            values[property.Metadata.Name] = original ? property.OriginalValue : property.CurrentValue;
        }

        return JsonSerializer.Serialize(values);
    }
}
