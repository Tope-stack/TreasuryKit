using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using TreasuryKit.Api.Demo;
using TreasuryKit.BuildingBlocks.Infrastructure.Jobs;
using TreasuryKit.BuildingBlocks.Infrastructure.Outbox;
using TreasuryKit.IntegrationTests.Core;
using TreasuryKit.IntegrationTests.Infrastructure;

namespace TreasuryKit.IntegrationTests.Api;

/// <summary>The demo seed on an empty database (ADR 0012), on a database of its own.</summary>
[Collection(ApiCollection.Name)]
public sealed class DemoSeederTests(ApiFactory factory)
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Empty_database_gets_a_reconciled_demo_tenant_exactly_once()
    {
        var database = $"demo_seed_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(factory.ConnectionString))
        {
            await admin.OpenAsync(_ct);
            await using var create = new NpgsqlCommand($"CREATE DATABASE {database}", admin);
            await create.ExecuteNonQueryAsync(_ct);
        }

        var connection = new NpgsqlConnectionStringBuilder(factory.ConnectionString) { Database = database }.ConnectionString;
        await using var app = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:TreasuryKit", connection);
            b.UseSetting(DemoSeeder.SettingKey, "true");
        });

        // Starting the host migrates and seeds; the background jobs import and reconcile.
        var client = app.CreateClient();
        await DrainAsync(app.Services);

        var tenants = await client.GetFromJsonAsync<List<TenantBody>>("/dev/tenants", _ct);
        var tenant = Assert.Single(tenants!);
        Assert.Equal(DemoSeeder.TenantName, tenant.Name);

        var token = await client.GetTokenAsync(tenant.Id, "amy", ["Approver"], _ct);
        client.WithBearer(token);
        var account = Assert.Single((await client.GetFromJsonAsync<List<AccountBody>>("/api/core/accounts", _ct))!);
        Assert.Equal(DemoSeeder.AccountNumber, account.AccountNumber);

        var runs = await client.GetFromJsonAsync<List<RunBody>>($"/api/recon/runs?accountId={account.Id}", CoreTestClient.Json, _ct);
        var run = Assert.Single(runs!);
        Assert.Equal("Completed", run.Status);
        Assert.Equal(64, run.Stats!.AutoConfirmed);
        Assert.True(run.Stats.Proposed > 0, "the demo should have proposals to review");

        // A second start on the same database does not seed again.
        Assert.Null(await DemoSeeder.SeedIfEmptyAsync(app.Services, NullLogger.Instance, _ct));
    }

    private async Task DrainAsync(IServiceProvider services)
    {
        var jobs = services.GetRequiredService<JobDispatcher>();
        var outbox = services.GetRequiredService<OutboxDispatcher>();
        for (var round = 0; round < 10; round++)
        {
            var work = await jobs.RunDueJobsAsync(_ct) + await outbox.DispatchPendingAsync(_ct);
            if (work == 0)
            {
                return;
            }
        }
    }

    private sealed record TenantBody(Guid Id, string Name);

    private sealed record AccountBody(Guid Id, string AccountNumber);

    private sealed record StatsBody(int AutoConfirmed, int Proposed);

    private sealed record RunBody(Guid Id, string Status, StatsBody? Stats);
}
