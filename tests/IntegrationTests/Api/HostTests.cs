using System.Net;
using System.Net.Http.Json;
using TreasuryKit.IntegrationTests.Infrastructure;

namespace TreasuryKit.IntegrationTests.Api;

[Collection(ApiCollection.Name)]
public sealed class HostTests(ApiFactory factory)
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Health_is_healthy_when_database_is_reachable()
    {
        var response = await factory.CreateClient().GetAsync("/health", _ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(_ct));
    }

    [Fact]
    public async Task Correlation_id_is_echoed()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("X-Correlation-ID", "abc-123");

        var response = await factory.CreateClient().SendAsync(request, _ct);

        Assert.Equal("abc-123", response.Headers.GetValues("X-Correlation-ID").Single());
    }

    [Fact]
    public async Task Validation_failures_are_problem_details()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/dev/tenants", new { name = "" }, _ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(_ct);
        Assert.Equal("Tenant.NameRequired", problem!.Title);
    }

    [Theory]
    [InlineData("/api/recon/matches?status=bogus")]
    [InlineData("/api/recon/exceptions?status=open")]
    [InlineData("/api/core/accounts/not-a-guid/lines")]
    public async Task Unbindable_parameters_are_400_not_500(string url)
    {
        var client = factory.CreateClient();
        var tenant = await client.CreateTenantAsync("Binding Ltd", _ct);
        client.WithBearer(await client.GetTokenAsync(tenant, "ada", ["Admin"], _ct));

        var response = await client.GetAsync(url, _ct);

        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound, $"{url} returned {(int)response.StatusCode}");
    }

    private sealed record ProblemBody(string Title, int Status);
}
