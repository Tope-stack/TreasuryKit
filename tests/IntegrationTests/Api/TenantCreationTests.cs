using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TreasuryKit.Core.Contracts.Tenants;
using TreasuryKit.Core.Infrastructure.Persistence;
using TreasuryKit.IntegrationTests.Infrastructure;

namespace TreasuryKit.IntegrationTests.Api;

[Collection(ApiCollection.Name)]
public sealed class TenantCreationTests(ApiFactory factory)
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Creating_a_tenant_writes_state_audit_and_outbox_atomically()
    {
        var tenantId = await factory.CreateClient().CreateTenantAsync("Atomic Ltd", _ct);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();

        var tenant = await db.Tenants.SingleAsync(t => t.Id == tenantId, _ct);
        Assert.Equal("Africa/Lagos", tenant.BusinessTimeZone);

        var audit = await db.AuditLog.SingleAsync(a => a.EntityType == "Tenant" && a.EntityId == tenantId.ToString(), _ct);
        Assert.Equal("Added", audit.Action);
        Assert.Null(audit.Before);
        Assert.Contains("Atomic Ltd", audit.After);

        var eventType = typeof(TenantCreated).FullName!;
        var outbox = await db.OutboxMessages
            .Where(m => m.TenantId == tenantId && m.Type.StartsWith(eventType))
            .ToListAsync(_ct);
        Assert.Single(outbox);
    }
}
