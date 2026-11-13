using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using TreasuryKit.BuildingBlocks.Infrastructure;
using TreasuryKit.IntegrationTests.Outbox;
using TreasuryKit.Recon.Infrastructure.Persistence;

namespace TreasuryKit.IntegrationTests.Infrastructure;

/// <summary>Hosts the real Api against a throwaway PostgreSQL container. Migrations run at startup (Development).</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public string ConnectionString => _postgres.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        _ = Services; // start the host so migrations are applied before any test runs
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:TreasuryKit", _postgres.GetConnectionString());
        builder.ConfigureTestServices(services =>
        {
            // Test subscribers, de-duplicated through the recon inbox like a real cross-module handler.
            services.AddSingleton<HandlerCallLog>();
            services.AddIntegrationEventHandler<TestEvent, CountingHandler, ReconDbContext>();
            services.AddIntegrationEventHandler<TestEvent, FlakyHandler, ReconDbContext>();
        });
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
