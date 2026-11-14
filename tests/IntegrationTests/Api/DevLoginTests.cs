using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using TreasuryKit.Api.Auth;

namespace TreasuryKit.IntegrationTests.Api;

/// <summary>When the development sign-in is available (ADRs 0003, 0010).</summary>
public sealed class DevLoginTests
{
    [Theory]
    [InlineData("Development", null, true)]
    [InlineData("Development", "false", true)]
    [InlineData("Demo", "true", true)]
    [InlineData("Demo", null, false)]
    [InlineData("Staging", "false", false)]
    [InlineData("Production", null, false)]
    public void Enabled_only_in_development_or_when_opted_in(string environment, string? flag, bool expected) =>
        Assert.Equal(expected, DevLogin.IsEnabled(Environment(environment), Configuration(flag)));

    [Fact]
    public void Refuses_to_start_in_production_with_the_flag_on()
    {
        var error = Assert.Throws<InvalidOperationException>(() => DevLogin.IsEnabled(Environment("Production"), Configuration("true")));
        Assert.Contains("must not be enabled in Production", error.Message, StringComparison.Ordinal);
    }

    private static IConfiguration Configuration(string? flag) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(flag is null ? [] : new Dictionary<string, string?> { [DevLogin.SettingKey] = flag })
            .Build();

    private static IHostEnvironment Environment(string name) => new TestEnvironment { EnvironmentName = name };

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";

        public string ApplicationName { get; set; } = "TreasuryKit.Api";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
