using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace TreasuryKit.IntegrationTests.Infrastructure;

internal static class HttpExtensions
{
    public static async Task<Guid> CreateTenantAsync(this HttpClient client, string name, CancellationToken ct)
    {
        var response = await client.PostAsJsonAsync("/dev/tenants", new { name }, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<TenantCreatedResponse>(ct);
        return body!.TenantId;
    }

    public static async Task<string> GetTokenAsync(this HttpClient client, Guid tenantId, string userId, string[] roles, CancellationToken ct)
    {
        var response = await client.PostAsJsonAsync("/dev/token", new { tenantId, userId, roles }, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<TokenResponse>(ct);
        return body!.AccessToken;
    }

    public static HttpClient WithBearer(this HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private sealed record TenantCreatedResponse(Guid TenantId);

    private sealed record TokenResponse(string AccessToken);
}
