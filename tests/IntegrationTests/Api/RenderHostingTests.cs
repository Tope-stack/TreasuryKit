using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TreasuryKit.Api.Middleware;
using TreasuryKit.BuildingBlocks.Infrastructure.Files;
using TreasuryKit.IntegrationTests.Infrastructure;

namespace TreasuryKit.IntegrationTests.Api;

/// <summary>The pieces that let the demo run as a single service on a free host (ADR 0011).</summary>
[Collection(ApiCollection.Name)]
public sealed class RenderHostingTests(ApiFactory factory)
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Database_file_store_saves_overwrites_and_reads_back()
    {
        var store = ActivatorUtilities.CreateInstance<DatabaseFileStore>(factory.Services);
        var key = $"{Guid.NewGuid():N}/statements/test.csv";

        await store.SaveAsync(key, new MemoryStream("first"u8.ToArray()), _ct);
        await store.SaveAsync(key, new MemoryStream("second"u8.ToArray()), _ct);

        await using var read = await store.OpenReadAsync(key, _ct);
        Assert.Equal("second", await new StreamReader(read).ReadToEndAsync(_ct));
        await Assert.ThrowsAsync<FileNotFoundException>(() => store.OpenReadAsync($"{key}.missing", _ct));
    }

    [Theory]
    [InlineData("/", null, 401)]
    [InlineData("/dev/token", null, 401)]
    [InlineData("/", "Basic wrong", 401)]
    [InlineData("/", "demo:open-sesame", 200)]
    [InlineData("/api/me", "Bearer token-checked-by-the-api", 200)]
    [InlineData("/health", null, 200)]
    public async Task Demo_gate_requires_the_password_unless_a_bearer_token_is_sent(string path, string? authorization, int expected)
    {
        var gate = new DemoGateMiddleware(
            context => { context.Response.StatusCode = 200; return Task.CompletedTask; },
            Options.Create(new DemoGateOptions { Username = "demo", Password = "open-sesame" }));
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        if (authorization is not null)
        {
            context.Request.Headers.Authorization = authorization.Contains(':', StringComparison.Ordinal)
                ? "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(authorization))
                : authorization;
        }

        await gate.InvokeAsync(context);

        Assert.Equal(expected, context.Response.StatusCode);
        if (expected == 401)
        {
            Assert.StartsWith("Basic", context.Response.Headers.WWWAuthenticate.ToString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Gate_is_off_without_a_password() => Assert.False(new DemoGateOptions().Enabled);
}
