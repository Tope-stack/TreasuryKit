using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TreasuryKit.BuildingBlocks.Infrastructure;
using TreasuryKit.BuildingBlocks.Infrastructure.Jobs;
using TreasuryKit.BuildingBlocks.Infrastructure.Outbox;
using TreasuryKit.BuildingBlocks.Infrastructure.Persistence;

namespace TreasuryKit.BuildingBlocks.Tests;

public sealed class ConnectionStringTests
{
    [Fact]
    public void Url_form_becomes_npgsql_keywords()
    {
        var result = ConnectionStrings.Normalize("postgresql://treasury%40kit:p%3Ass@db.internal:6543/treasurykit?sslmode=require");

        Assert.Contains("Host=db.internal", result, StringComparison.Ordinal);
        Assert.Contains("Port=6543", result, StringComparison.Ordinal);
        Assert.Contains("Database=treasurykit", result, StringComparison.Ordinal);
        Assert.Contains("Username=treasury@kit", result, StringComparison.Ordinal);
        Assert.Contains("Password=p:ss", result, StringComparison.Ordinal);
        Assert.Contains("SSL Mode=Require", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Default_port_is_5432() =>
        Assert.Contains("Port=5432", ConnectionStrings.Normalize("postgres://u:p@host/db"), StringComparison.Ordinal);

    [Theory]
    [InlineData("Host=localhost;Database=treasurykit")]
    [InlineData("  Host=localhost;Database=treasurykit ")]
    public void Keyword_form_is_kept(string value) =>
        Assert.Equal("Host=localhost;Database=treasurykit", ConnectionStrings.Normalize(value));

    [Fact]
    public void Missing_is_empty() => Assert.Equal(string.Empty, ConnectionStrings.Normalize(null));
}

public sealed class BackgroundProcessingTests
{
    [Fact]
    public void Registers_the_outbox_dispatcher_and_job_runner()
    {
        var hosted = new ServiceCollection().AddBackgroundProcessing()
            .Where(d => d.ServiceType == typeof(IHostedService))
            .Select(d => d.ImplementationType)
            .ToList();

        Assert.Equal([typeof(OutboxBackgroundService), typeof(JobBackgroundService)], hosted);
    }
}
