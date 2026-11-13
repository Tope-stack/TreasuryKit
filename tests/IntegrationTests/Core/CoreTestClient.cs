using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using TreasuryKit.BuildingBlocks.Infrastructure.Jobs;
using TreasuryKit.IntegrationTests.Infrastructure;

namespace TreasuryKit.IntegrationTests.Core;

/// <summary>A tenant with an Admin client and helpers for the Core statement-import flow.</summary>
internal sealed class CoreTestClient
{
    public static readonly Guid LayoutA = Guid.Parse("5e3d6a10-0000-4000-8000-00000000000a");
    public static readonly Guid LayoutB = Guid.Parse("5e3d6a10-0000-4000-8000-00000000000b");
    public static readonly Guid LayoutC = Guid.Parse("5e3d6a10-0000-4000-8000-00000000000c");

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static int _accountCounter;

    private readonly ApiFactory _factory;

    private CoreTestClient(ApiFactory factory, Guid tenantId, HttpClient admin)
    {
        _factory = factory;
        TenantId = tenantId;
        Admin = admin;
    }

    public Guid TenantId { get; }

    public HttpClient Admin { get; }

    public static async Task<CoreTestClient> CreateAsync(ApiFactory factory, CancellationToken ct)
    {
        var anonymous = factory.CreateClient();
        var tenantId = await anonymous.CreateTenantAsync($"Tenant {Guid.NewGuid():N}", ct);
        var token = await anonymous.GetTokenAsync(tenantId, "admin-user", ["Admin"], ct);
        return new CoreTestClient(factory, tenantId, factory.CreateClient().WithBearer(token));
    }

    public async Task<HttpClient> ClientAsAsync(string userId, string[] roles, CancellationToken ct) =>
        _factory.CreateClient().WithBearer(await _factory.CreateClient().GetTokenAsync(TenantId, userId, roles, ct));

    public async Task<Guid> CreateAccountAsync(Guid? mappingProfileId, CancellationToken ct, string? accountNumber = null)
    {
        var number = accountNumber ?? (1_000_000_000 + Interlocked.Increment(ref _accountCounter)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var response = await Admin.PostAsJsonAsync(
            "/api/core/accounts",
            new { bankCode = "058", bankName = "Demo Bank", accountNumber = number, currency = "NGN", mappingProfileId },
            ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IdResponse>(ct))!.Id;
    }

    public static async Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid accountId, string goldenFile, CancellationToken ct)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "statements", goldenFile);
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(await File.ReadAllBytesAsync(path, ct));
        form.Add(file, "file", Path.GetFileName(goldenFile));
        return await client.PostAsync($"/api/core/accounts/{accountId}/statements", form, ct);
    }

    /// <summary>Uploads a golden file, runs the Worker's job loop once, and returns the import.</summary>
    public async Task<ImportBody> ImportAsync(Guid accountId, string goldenFile, CancellationToken ct)
    {
        var response = await UploadAsync(Admin, accountId, goldenFile, ct);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var upload = (await response.Content.ReadFromJsonAsync<UploadBody>(ct))!;

        await RunJobsAsync(ct);
        return await GetImportAsync(upload.ImportId, ct);
    }

    /// <summary>Runs due jobs until none are left (the dispatcher claims a small batch per call).</summary>
    public async Task RunJobsAsync(CancellationToken ct)
    {
        var dispatcher = _factory.Services.GetRequiredService<JobDispatcher>();
        while (await dispatcher.RunDueJobsAsync(ct) > 0)
        {
        }
    }

    public async Task<ImportBody> GetImportAsync(Guid importId, CancellationToken ct) =>
        (await Admin.GetFromJsonAsync<ImportBody>($"/api/core/imports/{importId}", Json, ct))!;

    public async Task<LinesBody> GetLinesAsync(Guid accountId, CancellationToken ct) =>
        (await Admin.GetFromJsonAsync<LinesBody>($"/api/core/accounts/{accountId}/lines?pageSize=500", Json, ct))!;

    internal sealed record IdResponse(Guid Id);

    internal sealed record UploadBody(Guid ImportId, bool AlreadyImported);

    internal sealed record ImportBody(
        Guid Id,
        Guid AccountId,
        string Status,
        int LineCount,
        int DuplicateLineCount,
        int SkippedEntryCount,
        bool BalanceValidated,
        decimal? OpeningBalance,
        decimal? ClosingBalance,
        DateOnly? PeriodStart,
        DateOnly? PeriodEnd,
        string? Error,
        string? AcknowledgedBy);

    internal sealed record LineBody(Guid Id, DateOnly ValueDate, decimal Amount, string Narration, string? BankReference);

    internal sealed record LinesBody(List<LineBody> Items, int TotalCount);
}
