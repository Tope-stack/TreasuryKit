using Microsoft.EntityFrameworkCore;
using TreasuryKit.Recon.Domain.Configuration;

namespace TreasuryKit.Recon.Application.Configuration;

/// <summary>Seeds default rule configs and thresholds for a tenant, once (plan §5.1 "seeded defaults per tenant").</summary>
public sealed class ReconDefaults(IReconDbContext db)
{
    /// <summary>Adds missing defaults to the context (caller saves) and returns the tenant's configuration.</summary>
    public async Task<(IReadOnlyList<MatchingRuleConfig> Rules, ReconSettings Settings)> EnsureAsync(Guid tenantId, CancellationToken ct)
    {
        var rules = await db.RuleConfigs.Where(r => r.TenantId == tenantId).ToListAsync(ct);
        foreach (var missing in MatchingRuleConfig.CreateDefaults(tenantId).Where(d => rules.All(r => r.RuleKey != d.RuleKey)))
        {
            db.RuleConfigs.Add(missing);
            rules.Add(missing);
        }

        var settings = await db.Settings.FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
        if (settings is null)
        {
            settings = ReconSettings.CreateDefault(tenantId);
            db.Settings.Add(settings);
        }

        return (rules.OrderBy(r => r.Order).ToList(), settings);
    }
}
