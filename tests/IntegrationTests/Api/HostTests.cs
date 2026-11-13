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

    private sealed record ProblemBody(string Title, int Status);
}
