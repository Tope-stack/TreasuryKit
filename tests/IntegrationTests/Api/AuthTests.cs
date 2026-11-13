using System.Net;
using System.Net.Http.Json;
using TreasuryKit.IntegrationTests.Infrastructure;

namespace TreasuryKit.IntegrationTests.Api;

[Collection(ApiCollection.Name)]
public sealed class AuthTests(ApiFactory factory)
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Anonymous_requests_to_api_are_rejected()
    {
        var response = await factory.CreateClient().GetAsync("/api/me", _ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Dev_token_authenticates_with_tenant_user_and_roles()
    {
        var client = factory.CreateClient();
        var tenantId = await client.CreateTenantAsync("Auth Test Ltd", _ct);
        var token = await client.GetTokenAsync(tenantId, "alice", ["Preparer", "Approver"], _ct);

        var me = await client.WithBearer(token).GetFromJsonAsync<MeResponse>("/api/me", _ct);

        Assert.Equal("alice", me!.UserId);
        Assert.Equal(tenantId, me.TenantId);
        Assert.Equal(["Approver", "Preparer"], me.Roles);
    }

    [Fact]
    public async Task Token_with_wrong_signature_is_rejected()
    {
        var client = factory.CreateClient();
        var token = await client.GetTokenAsync(Guid.NewGuid(), "mallory", ["Admin"], _ct);
        var tampered = token[..^4] + (token.EndsWith("AAAA", StringComparison.Ordinal) ? "BBBB" : "AAAA");

        var response = await client.WithBearer(tampered).GetAsync("/api/me", _ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Dev_token_rejects_unknown_roles()
    {
        var response = await factory.CreateClient().PostAsJsonAsync(
            "/dev/token",
            new { tenantId = Guid.NewGuid(), userId = "bob", roles = new[] { "SuperUser" } },
            _ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed record MeResponse(string UserId, Guid TenantId, string[] Roles);
}
