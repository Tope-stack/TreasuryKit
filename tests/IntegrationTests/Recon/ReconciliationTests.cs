using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TreasuryKit.BuildingBlocks.Infrastructure.Outbox;
using TreasuryKit.IntegrationTests.Core;
using TreasuryKit.IntegrationTests.Infrastructure;

namespace TreasuryKit.IntegrationTests.Recon;

/// <summary>Phase 3 acceptance (plan §6) on the generated reconciliation dataset (plan §7), end to end through the API and Worker.</summary>
[Collection(ApiCollection.Name)]
public sealed class ReconciliationTests(ApiFactory factory)
{
    private const string AccountNumber = "0123456789";

    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Imported_statement_is_reconciled_automatically_with_full_precision()
    {
        var (core, account) = await ArrangeAsync();

        var outcome = await OutcomesAsync(core, account);

        // ≥ 90% of clean items auto-match (we expect all of them).
        var clean = AnswerKey.Items.Where(i => i.Clean).ToList();
        var cleanAuto = clean.Count(i => outcome.Of(i) == "AutoConfirmed");
        Assert.True(cleanAuto >= 0.9 * clean.Count, $"{cleanAuto}/{clean.Count} clean items auto-matched");

        // 100% precision: every auto-confirmed match pairs items that truly belong together.
        Assert.NotEmpty(outcome.Confirmed);
        foreach (var match in outcome.Confirmed)
        {
            Assert.Contains(AnswerKey.Items, i => match.BankReferences.All(i.BankReferences.Contains) && match.ExternalIds.All(i.ExternalIds.Contains));
        }

        // Every scenario ends where the design says it should.
        var wrong = AnswerKey.Items.Where(i => outcome.Of(i) != i.Outcome).Select(i => $"{i.Scenario}: expected {i.Outcome}, got {outcome.Of(i)}").ToList();
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    [Fact]
    public async Task Confirmed_matches_settle_expected_transactions()
    {
        var (core, _) = await ArrangeAsync();
        await DispatchOutboxAsync(); // MatchConfirmed → Core

        var expected = (await core.Admin.GetFromJsonAsync<List<ExpectedBody>>("/api/core/expected-transactions", CoreTestClient.Json, _ct))!;
        var cleanIds = AnswerKey.Items.Where(i => i.Clean).SelectMany(i => i.ExternalIds).ToHashSet();
        var unpaidIds = AnswerKey.Items.Where(i => i.Scenario == "UnmatchedExpected").SelectMany(i => i.ExternalIds).ToHashSet();

        Assert.All(expected.Where(e => cleanIds.Contains(e.ExternalId!)), e =>
        {
            Assert.Equal("Settled", e.Status);
            Assert.Equal(0m, e.OutstandingAmount);
        });
        Assert.All(expected.Where(e => unpaidIds.Contains(e.ExternalId!)), e => Assert.Equal("Open", e.Status));
    }

    [Fact]
    public async Task Rerunning_creates_no_duplicate_matches_or_exceptions()
    {
        var (core, account) = await ArrangeAsync();
        var before = await SnapshotAsync(core, account);

        var first = (await core.Admin.GetFromJsonAsync<List<RunBody>>($"/api/recon/runs?accountId={account}", CoreTestClient.Json, _ct))!.Single();

        // Same period as the automatic run: a wider period would legitimately add exceptions for more expected items.
        var rerun = await core.Admin.PostAsJsonAsync("/api/recon/runs", new { accountId = account, from = first.PeriodStart, to = first.PeriodEnd }, _ct);
        Assert.Equal(HttpStatusCode.Accepted, rerun.StatusCode);
        await core.RunJobsAsync(_ct);
        var after = await SnapshotAsync(core, account);

        Assert.Equal(before.ConfirmedPairs, after.ConfirmedPairs);
        Assert.Equal(before.ProposedPairs, after.ProposedPairs); // re-proposed after superseding
        Assert.Equal(before.OpenExceptionItems, after.OpenExceptionItems);
        Assert.Equal(after.ConfirmedPairs.Count + after.ProposedPairs.Count, after.ActiveMatchCount);
        Assert.Equal(before.ProposedPairs.Count, after.SupersededCount);
    }

    [Fact]
    public async Task Concurrent_run_requests_for_one_account_start_one_run()
    {
        var core = await CoreTestClient.CreateAsync(factory, _ct);
        var account = await core.CreateAccountAsync(CoreTestClient.LayoutA, _ct, AccountNumber);
        var body = new { accountId = account, from = "2026-02-01", to = "2026-02-28" };

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => core.Admin.PostAsJsonAsync("/api/recon/runs", body, _ct)));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Accepted));
        Assert.Equal(3, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        var conflict = await responses.First(r => r.StatusCode == HttpStatusCode.Conflict).Content.ReadAsStringAsync(_ct);
        Assert.Contains("Run.AlreadyActive", conflict, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_is_blocked_by_an_unacknowledged_balance_mismatch()
    {
        var core = await CoreTestClient.CreateAsync(factory, _ct);
        var account = await core.CreateAccountAsync(CoreTestClient.LayoutA, _ct);
        Assert.Equal("BalanceMismatch", (await core.ImportAsync(account, "layout-a/unbalanced.csv", _ct)).Status);

        var response = await core.Admin.PostAsJsonAsync("/api/recon/runs", new { accountId = account, from = "2026-01-01", to = "2026-01-31" }, _ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("Run.Blocked", await response.Content.ReadAsStringAsync(_ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Expected_transaction_csv_is_idempotent_by_external_id()
    {
        var core = await CoreTestClient.CreateAsync(factory, _ct);
        await core.CreateAccountAsync(CoreTestClient.LayoutA, _ct, AccountNumber);
        var csv = await File.ReadAllTextAsync(DataPath("expected.csv"), _ct);

        var first = await ImportExpectedAsync(core, csv, "expected.csv");
        Assert.Equal(AnswerKey.ExpectedCount, first.Created);
        Assert.Empty(first.Errors);

        var same = await UploadExpectedAsync(core, csv, "expected.csv");
        Assert.Equal(HttpStatusCode.OK, same.StatusCode); // identical file: the earlier import

        // A re-export with one changed amount and one bad row.
        var lines = csv.TrimEnd('\n').Split('\n').ToList();
        var columns = lines[1].Split(',');
        columns[3] = "123.45";
        lines[1] = string.Join(',', columns);
        lines.Add("EXP-BAD,,Sideways,10.00,NGN,2026-02-10,,,");
        var second = await ImportExpectedAsync(core, string.Join('\n', lines) + "\n", "expected-v2.csv");

        Assert.Equal(0, second.Created);
        Assert.Equal(1, second.Updated);
        Assert.Equal(AnswerKey.ExpectedCount - 1, second.Unchanged);
        var error = Assert.Single(second.Errors);
        Assert.Contains("direction", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Roles_and_tenants_are_enforced()
    {
        var (core, account) = await ArrangeAsync();
        var viewer = await core.ClientAsAsync("vic", ["Viewer"], _ct);
        var preparer = await core.ClientAsAsync("pat", ["Preparer"], _ct);
        var other = await CoreTestClient.CreateAsync(factory, _ct);
        var runs = await core.Admin.GetFromJsonAsync<List<RunBody>>($"/api/recon/runs?accountId={account}", CoreTestClient.Json, _ct);

        var viewerRun = await viewer.PostAsJsonAsync("/api/recon/runs", new { accountId = account, from = "2026-02-01", to = "2026-02-02" }, _ct);
        Assert.Equal(HttpStatusCode.Forbidden, viewerRun.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/recon/rules", _ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await preparer.PutAsJsonAsync("/api/recon/rules/AmountDateWindow", new { enabled = true, order = 2 }, _ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await core.Admin.PutAsJsonAsync(
            "/api/recon/rules/AmountDateWindow", new { enabled = true, order = 2, parameters = new { windowDays = 99 } }, _ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await core.Admin.PutAsJsonAsync("/api/recon/settings/thresholds", new { autoConfirm = 0.97, propose = 0.75 }, _ct)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await other.Admin.GetAsync($"/api/recon/runs/{runs![0].Id}", _ct)).StatusCode);
        Assert.Empty((await other.Admin.GetFromJsonAsync<List<MatchBody>>("/api/recon/matches", CoreTestClient.Json, _ct))!);
        Assert.Empty((await other.Admin.GetFromJsonAsync<List<ExceptionBody>>("/api/recon/exceptions", CoreTestClient.Json, _ct))!);
    }

    /// <summary>Tenant + account + expected transactions + statement; the import's event triggers the run.</summary>
    private async Task<(CoreTestClient Core, Guid Account)> ArrangeAsync()
    {
        var core = await CoreTestClient.CreateAsync(factory, _ct);
        await DispatchOutboxAsync(); // TenantCreated → Recon seeds rules
        var account = await core.CreateAccountAsync(CoreTestClient.LayoutA, _ct, AccountNumber);

        var expected = await ImportExpectedAsync(core, await File.ReadAllTextAsync(DataPath("expected.csv"), _ct), "expected.csv");
        Assert.Empty(expected.Errors);

        var response = await CoreTestClient.UploadAsync(core.Admin, account, Path.Combine("..", "recon", "statement.csv"), _ct);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await core.RunJobsAsync(_ct);   // import the statement → StatementImported
        await DispatchOutboxAsync();    // Recon queues a run
        await core.RunJobsAsync(_ct);   // the run executes

        var runs = await core.Admin.GetFromJsonAsync<List<RunBody>>($"/api/recon/runs?accountId={account}", CoreTestClient.Json, _ct);
        var run = Assert.Single(runs!);
        Assert.Equal("Completed", run.Status);
        Assert.Equal("Event", run.TriggeredBy);
        return (core, account);
    }

    private async Task<Outcomes> OutcomesAsync(CoreTestClient core, Guid account)
    {
        var lines = (await core.GetLinesAsync(account, _ct)).Items.ToDictionary(l => l.Id, l => l.BankReference!);
        var expected = (await core.Admin.GetFromJsonAsync<List<ExpectedBody>>("/api/core/expected-transactions", CoreTestClient.Json, _ct))!
            .ToDictionary(e => e.Id, e => e.ExternalId!);
        var matches = (await core.Admin.GetFromJsonAsync<List<MatchBody>>($"/api/recon/matches?accountId={account}", CoreTestClient.Json, _ct))!;

        MatchView View(MatchBody m) => new(
            m.Status,
            m.Items.Where(i => i.ItemType == "StatementLine").Select(i => lines[i.ItemId]).ToList(),
            m.Items.Where(i => i.ItemType == "ExpectedTransaction").Select(i => expected[i.ItemId]).ToList());

        return new Outcomes(matches.Where(m => m.Status is "Confirmed" or "Proposed").Select(View).ToList());
    }

    private async Task<Snapshot> SnapshotAsync(CoreTestClient core, Guid account)
    {
        var matches = (await core.Admin.GetFromJsonAsync<List<MatchBody>>($"/api/recon/matches?accountId={account}", CoreTestClient.Json, _ct))!;
        var exceptions = (await core.Admin.GetFromJsonAsync<List<ExceptionBody>>($"/api/recon/exceptions?accountId={account}&status=Open", CoreTestClient.Json, _ct))!;
        static string Pair(MatchBody m) => string.Join("+", m.Items.Select(i => i.ItemId).Order());
        return new Snapshot(
            matches.Where(m => m.Status == "Confirmed").Select(Pair).Order().ToList(),
            matches.Where(m => m.Status == "Proposed").Select(Pair).Order().ToList(),
            exceptions.Select(e => e.ItemId).Order().ToList(),
            matches.Count(m => m.Status is "Confirmed" or "Proposed"),
            matches.Count(m => m.Status == "Rejected" && m.Reason == "Superseded"));
    }

    private async Task<ExpectedImportBody> ImportExpectedAsync(CoreTestClient core, string csv, string fileName)
    {
        var response = await UploadExpectedAsync(core, csv, fileName);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var importId = JsonDocument.Parse(await response.Content.ReadAsStringAsync(_ct)).RootElement.GetProperty("importId").GetGuid();
        await core.RunJobsAsync(_ct);
        var import = await core.Admin.GetFromJsonAsync<ExpectedImportBody>($"/api/core/expected-transactions/imports/{importId}", CoreTestClient.Json, _ct);
        Assert.Equal("Completed", import!.Status);
        return import;
    }

    private async Task<HttpResponseMessage> UploadExpectedAsync(CoreTestClient core, string csv, string fileName)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(csv), "file", fileName);
        return await core.Admin.PostAsync("/api/core/expected-transactions/import", form, _ct);
    }

    /// <summary>Delivers outbox messages until none are left (one batch per call; other tests leave messages behind).</summary>
    private async Task DispatchOutboxAsync()
    {
        var dispatcher = factory.Services.GetRequiredService<OutboxDispatcher>();
        while (await dispatcher.DispatchPendingAsync(_ct) > 0)
        {
        }
    }

    private static string DataPath(string file) => Path.Combine(AppContext.BaseDirectory, "TestData", "recon", file);

    private sealed record MatchView(string Status, IReadOnlyList<string> BankReferences, IReadOnlyList<string> ExternalIds);

    private sealed record Outcomes(IReadOnlyList<MatchView> Matches)
    {
        public IEnumerable<MatchView> Confirmed => Matches.Where(m => m.Status == "Confirmed");

        /// <summary>AutoConfirmed / Proposed when a match covers exactly this item's pairing; WrongPairing if it touches it otherwise; else Unmatched.</summary>
        public string Of(AnswerKey.Item item)
        {
            var match = Matches.FirstOrDefault(m =>
                m.BankReferences.Intersect(item.BankReferences).Any() || m.ExternalIds.Intersect(item.ExternalIds).Any());
            if (match is null)
            {
                return "Unmatched";
            }

            var exact = match.BankReferences.Order().SequenceEqual(item.BankReferences.Order())
                && match.ExternalIds.Order().SequenceEqual(item.ExternalIds.Order());
            return !exact ? "WrongPairing" : match.Status == "Confirmed" ? "AutoConfirmed" : "Proposed";
        }
    }

    private sealed record Snapshot(
        List<string> ConfirmedPairs, List<string> ProposedPairs, List<Guid> OpenExceptionItems, int ActiveMatchCount, int SupersededCount);

    private sealed record RunBody(Guid Id, string Status, string TriggeredBy, DateOnly PeriodStart, DateOnly PeriodEnd);

    private sealed record MatchItemBody(string ItemType, Guid ItemId, decimal AllocatedAmount);

    private sealed record MatchBody(Guid Id, string Status, string RuleKey, decimal Confidence, string? Reason, List<MatchItemBody> Items);

    private sealed record ExceptionBody(Guid Id, string ItemType, Guid ItemId, string Reason, string Status);

    private sealed record ExpectedBody(Guid Id, string? ExternalId, string Status, decimal OutstandingAmount);

    private sealed record RowError(int Row, string Message);

    private sealed record ExpectedImportBody(Guid Id, string Status, int Created, int Updated, int Unchanged, int Skipped, List<RowError> Errors);
}

internal static class AnswerKey
{
    public sealed record Item(string Scenario, bool Clean, string Outcome, List<string> ExternalIds, List<string> BankReferences);

    public static readonly IReadOnlyList<Item> Items = JsonSerializer.Deserialize<List<Item>>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "recon", "answer-key.json")),
        new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    public static int ExpectedCount => Items.SelectMany(i => i.ExternalIds).Count();
}
