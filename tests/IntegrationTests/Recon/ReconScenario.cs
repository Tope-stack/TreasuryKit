using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TreasuryKit.BuildingBlocks.Infrastructure.Outbox;
using TreasuryKit.IntegrationTests.Core;
using TreasuryKit.IntegrationTests.Infrastructure;

namespace TreasuryKit.IntegrationTests.Recon;

/// <summary>A tenant with the reconciliation dataset imported and reconciled, plus lookups between ids and dataset keys.</summary>
internal sealed class ReconScenario
{
    public const string AccountNumber = "0123456789";

    private readonly ApiFactory _factory;
    private readonly CancellationToken _ct;

    private ReconScenario(ApiFactory factory, CoreTestClient core, Guid account, CancellationToken ct)
    {
        _factory = factory;
        Core = core;
        Account = account;
        _ct = ct;
    }

    public CoreTestClient Core { get; }

    public Guid Account { get; }

    public Dictionary<string, Guid> LineIdByReference { get; private set; } = [];

    public Dictionary<string, Guid> ExpectedIdByExternalId { get; private set; } = [];

    public static async Task<ReconScenario> CreateAsync(ApiFactory factory, CancellationToken ct)
    {
        var core = await CoreTestClient.CreateAsync(factory, ct);
        await DrainOutboxAsync(factory, ct);
        var account = await core.CreateAccountAsync(CoreTestClient.LayoutA, ct, AccountNumber);

        using (var form = new MultipartFormDataContent())
        {
            form.Add(new ByteArrayContent(await File.ReadAllBytesAsync(DataPath("expected.csv"), ct)), "file", "expected.csv");
            Assert.Equal(HttpStatusCode.Accepted, (await core.Admin.PostAsync("/api/core/expected-transactions/import", form, ct)).StatusCode);
        }

        await core.RunJobsAsync(ct);
        Assert.Equal(HttpStatusCode.Accepted, (await CoreTestClient.UploadAsync(core.Admin, account, Path.Combine("..", "recon", "statement.csv"), ct)).StatusCode);
        await core.RunJobsAsync(ct);
        await DrainOutboxAsync(factory, ct);
        await core.RunJobsAsync(ct);

        var scenario = new ReconScenario(factory, core, account, ct);
        await scenario.LoadIdsAsync();
        return scenario;
    }

    public static string DataPath(string file) => Path.Combine(AppContext.BaseDirectory, "TestData", "recon", file);

    public static async Task DrainOutboxAsync(ApiFactory factory, CancellationToken ct)
    {
        var dispatcher = factory.Services.GetRequiredService<OutboxDispatcher>();
        while (await dispatcher.DispatchPendingAsync(ct) > 0)
        {
        }
    }

    public Task DrainOutboxAsync() => DrainOutboxAsync(_factory, _ct);

    public async Task<List<MatchBody>> MatchesAsync(string? status = null) =>
        (await Core.Admin.GetFromJsonAsync<List<MatchBody>>(
            $"/api/recon/matches?accountId={Account}{(status is null ? string.Empty : "&status=" + status)}", CoreTestClient.Json, _ct))!;

    public async Task<List<ExceptionBody>> ExceptionsAsync(string status = "Open") =>
        (await Core.Admin.GetFromJsonAsync<List<ExceptionBody>>($"/api/recon/exceptions?accountId={Account}&status={status}", CoreTestClient.Json, _ct))!;

    public async Task<ExpectedBody> ExpectedAsync(string externalId) =>
        (await Core.Admin.GetFromJsonAsync<ExpectedBody>($"/api/core/expected-transactions/{ExpectedIdByExternalId[externalId]}", CoreTestClient.Json, _ct))!;

    /// <summary>The match (proposed or confirmed) that covers a dataset item, if any.</summary>
    public async Task<MatchBody?> ActiveMatchForAsync(AnswerKey.Item item)
    {
        var ids = item.BankReferences.Select(r => LineIdByReference[r]).Concat(item.ExternalIds.Select(e => ExpectedIdByExternalId[e])).ToHashSet();
        return (await MatchesAsync()).FirstOrDefault(m => m.Status is "Proposed" or "Confirmed" && m.Items.Any(i => ids.Contains(i.ItemId)));
    }

    public async Task<HttpResponseMessage> RunAsync()
    {
        var response = await Core.Admin.PostAsJsonAsync("/api/recon/runs", new { accountId = Account, from = "2026-02-01", to = "2026-02-28" }, _ct);
        await Core.RunJobsAsync(_ct);
        return response;
    }

    private async Task LoadIdsAsync()
    {
        LineIdByReference = (await Core.GetLinesAsync(Account, _ct)).Items.ToDictionary(l => l.BankReference!, l => l.Id);
        ExpectedIdByExternalId = (await Core.Admin.GetFromJsonAsync<List<ExpectedBody>>("/api/core/expected-transactions", CoreTestClient.Json, _ct))!
            .ToDictionary(e => e.ExternalId!, e => e.Id);
    }

    internal sealed record MatchItemBody(string ItemType, Guid ItemId, decimal AllocatedAmount);

    internal sealed record MatchBody(
        Guid Id, string Status, string RuleKey, string Type, decimal Confidence, string? Reason, string ProposedBy, string? ConfirmedBy,
        decimal Adjustment, List<MatchItemBody> Items);

    internal sealed record ExceptionBody(Guid Id, string ItemType, Guid ItemId, string Reason, string Status);

    internal sealed record ExpectedBody(Guid Id, string? ExternalId, string Status, decimal Amount, decimal OutstandingAmount);
}

internal static class JsonExtensions
{
    public static async Task<JsonElement> JsonAsync(this HttpResponseMessage response, CancellationToken ct) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)).RootElement;
}
