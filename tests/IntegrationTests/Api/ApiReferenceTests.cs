using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using TreasuryKit.Api.OpenApi;
using TreasuryKit.IntegrationTests.Infrastructure;

namespace TreasuryKit.IntegrationTests.Api;

[Collection(ApiCollection.Name)]
public sealed class ApiReferenceTests(ApiFactory factory)
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("Development", null, true)]
    [InlineData("Demo", "true", true)]
    [InlineData("Demo", null, false)]
    [InlineData("Production", null, false)]
    public void Enabled_in_development_or_when_opted_in(string environment, string? flag, bool expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(flag is null ? [] : new Dictionary<string, string?> { [ApiReference.SettingKey] = flag })
            .Build();

        Assert.Equal(expected, ApiReference.IsEnabled(new TestEnvironment { EnvironmentName = environment }, configuration));
    }

    [Fact]
    public void Demo_settings_turn_the_reference_on()
    {
        var demo = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.Demo.json"), optional: false)
            .Build();

        Assert.True(ApiReference.IsEnabled(new TestEnvironment { EnvironmentName = "Demo" }, demo));
    }

    [Fact]
    public async Task Document_declares_bearer_authentication_and_the_reference_is_served()
    {
        var client = factory.CreateClient();

        var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json", _ct)).RootElement;
        var scheme = document.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.Equal("http", scheme.GetProperty("type").GetString());
        Assert.Equal("bearer", scheme.GetProperty("scheme").GetString());
        Assert.Contains(document.GetProperty("security").EnumerateArray(), r => r.TryGetProperty("Bearer", out _));

        var reference = await client.GetAsync("/scalar/v1", _ct);
        Assert.Equal(HttpStatusCode.OK, reference.StatusCode);
        Assert.Contains("text/html", reference.Content.Headers.ContentType?.ToString(), StringComparison.Ordinal);
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";

        public string ApplicationName { get; set; } = "TreasuryKit.Api";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
