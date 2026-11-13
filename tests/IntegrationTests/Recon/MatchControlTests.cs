using System.Net;
using System.Net.Http.Json;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TreasuryKit.BuildingBlocks.Application.Abstractions;
using TreasuryKit.BuildingBlocks.Infrastructure.Context;
using TreasuryKit.BuildingBlocks.Infrastructure.Idempotency;
using TreasuryKit.IntegrationTests.Core;
using TreasuryKit.IntegrationTests.Infrastructure;

namespace TreasuryKit.IntegrationTests.Recon;

/// <summary>Phase 4 acceptance (plan §6) and the controls around it: maker-checker, reversal, bulk confirm, reports, idempotency.</summary>
[Collection(ApiCollection.Name)]
public sealed class MatchControlTests(ApiFactory factory)
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Split_payments_of_2_to_5_parts_are_proposed_with_the_right_parts()
    {
        var s = await ReconScenario.CreateAsync(factory, _ct);

        foreach (var item in AnswerKey.Items.Where(i => i.Scenario.StartsWith("Split", StringComparison.Ordinal)))
        {
            var match = await s.ActiveMatchForAsync(item);
            Assert.NotNull(match);
            Assert.Equal("Proposed", match.Status);
            Assert.Equal("SplitPayment", match.RuleKey);
            Assert.Equal("ManyToOne", match.Type);
            Assert.Equal(
                item.BankReferences.Select(r => s.LineIdByReference[r]).Order(),
                match.Items.Where(i => i.ItemType == "StatementLine").Select(i => i.ItemId).Order());
        }

        Assert.Equal([2, 3, 4, 5], AnswerKey.Items.Where(i => i.Scenario.StartsWith("Split", StringComparison.Ordinal)).Select(i => i.BankReferences.Count).Order());
    }

    [Fact]
    public async Task Bank_charge_deductions_are_proposed_with_an_adjustment()
    {
        var s = await ReconScenario.CreateAsync(factory, _ct);

        foreach (var item in AnswerKey.Items.Where(i => i.Scenario == "BankChargeDeducted"))
        {
            var match = await s.ActiveMatchForAsync(item);
            Assert.NotNull(match);
            Assert.Equal(("Proposed", "Tolerance"), (match.Status, match.RuleKey));
            Assert.True(match.Adjustment < 0m, "the bank paid in less than invoiced");
        }
    }

    [Fact]
    public async Task Same_user_cannot_propose_and_confirm_a_manual_match()
    {
        var s = await ReconScenario.CreateAsync(factory, _ct);
        var item = AnswerKey.Items.First(i => i.Scenario == "DateDrift4"); // same amount, 4 days apart: no rule matches it
        var both = await s.Core.ClientAsAsync("dual-role", ["Preparer", "Approver"], _ct);
        var approver = await s.Core.ClientAsAsync("amy", ["Approver"], _ct);

        var proposed = await both.PostAsJsonAsync("/api/recon/matches", Body(s, item, "Paid late"), _ct);
        Assert.Equal(HttpStatusCode.Created, proposed.StatusCode);
        var matchId = (await proposed.JsonAsync(_ct)).GetProperty("matchId").GetGuid();

        var self = await both.PostAsync($"/api/recon/matches/{matchId}/confirm", null, _ct);
        Assert.Equal(HttpStatusCode.Forbidden, self.StatusCode);
        Assert.Contains("Match.MakerChecker", await self.Content.ReadAsStringAsync(_ct), StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.NoContent, (await approver.PostAsync($"/api/recon/matches/{matchId}/confirm", null, _ct)).StatusCode);
        await s.DrainOutboxAsync();
        Assert.Equal("Settled", (await s.ExpectedAsync(item.ExternalIds[0])).Status);

        // The open exceptions for those items were resolved by the manual match.
        var ids = item.BankReferences.Select(r => s.LineIdByReference[r]).Concat(item.ExternalIds.Select(e => s.ExpectedIdByExternalId[e])).ToHashSet();
        Assert.DoesNotContain(await s.ExceptionsAsync(), e => ids.Contains(e.ItemId));
    }

    [Fact]
    public async Task Reversal_returns_items_to_the_pool_and_restores_the_outstanding_amount()
    {
        var s = await ReconScenario.CreateAsync(factory, _ct);
        await s.DrainOutboxAsync(); // settle auto-confirmed matches
        var item = AnswerKey.Items.First(i => i.Scenario == "CleanInvoice");
        var match = (await s.ActiveMatchForAsync(item))!;
        Assert.Equal("Confirmed", match.Status);
        Assert.Equal("Settled", (await s.ExpectedAsync(item.ExternalIds[0])).Status);

        var noReason = await s.Core.Admin.PostAsJsonAsync($"/api/recon/matches/{match.Id}/reverse", new { reason = "" }, _ct);
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        var reversed = await s.Core.Admin.PostAsJsonAsync($"/api/recon/matches/{match.Id}/reverse", new { reason = "Posted to the wrong invoice" }, _ct);
        Assert.Equal(HttpStatusCode.NoContent, reversed.StatusCode);
        await s.DrainOutboxAsync();

        var expected = await s.ExpectedAsync(item.ExternalIds[0]);
        Assert.Equal("Open", expected.Status);
        Assert.Equal(expected.Amount, expected.OutstandingAmount);
        Assert.Null(await s.ActiveMatchForAsync(item));

        // Back in the pool: the next run finds the pairing again.
        Assert.Equal(HttpStatusCode.Accepted, (await s.RunAsync()).StatusCode);
        var rematched = await s.ActiveMatchForAsync(item);
        Assert.NotNull(rematched);
        Assert.NotEqual(match.Id, rematched.Id);
    }

    [Fact]
    public async Task Rejected_proposal_is_not_proposed_again()
    {
        var s = await ReconScenario.CreateAsync(factory, _ct);
        var item = AnswerKey.Items.First(i => i.Scenario == "NoNameNoReference");
        var proposal = (await s.ActiveMatchForAsync(item))!;

        Assert.Equal(HttpStatusCode.NoContent, (await s.Core.Admin.PostAsJsonAsync($"/api/recon/matches/{proposal.Id}/reject", new { reason = "Not this customer" }, _ct)).StatusCode);
        await s.RunAsync();

        Assert.Null(await s.ActiveMatchForAsync(item));
        Assert.Contains(await s.ExceptionsAsync(), e => e.ItemId == s.LineIdByReference[item.BankReferences[0]]);
    }

    [Fact]
    public async Task Bulk_confirm_reports_each_match()
    {
        var s = await ReconScenario.CreateAsync(factory, _ct);
        var drift = AnswerKey.Items.First(i => i.Scenario == "DateDrift5");
        var approverPreparer = await s.Core.ClientAsAsync("pat", ["Preparer", "Approver"], _ct);
        var manual = (await (await approverPreparer.PostAsJsonAsync("/api/recon/matches", Body(s, drift, null), _ct)).JsonAsync(_ct)).GetProperty("matchId").GetGuid();
        var splits = (await s.MatchesAsync("Proposed")).Where(m => m.RuleKey == "SplitPayment").Select(m => m.Id).ToList();

        var response = await approverPreparer.PostAsJsonAsync("/api/recon/matches/confirm-batch", new { matchIds = splits.Append(manual).Append(Guid.NewGuid()) }, _ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.JsonAsync(_ct);
        Assert.Equal(splits.Order(), body.GetProperty("confirmed").EnumerateArray().Select(e => e.GetGuid()).Order());
        var failed = body.GetProperty("failed").EnumerateArray().ToDictionary(f => f.GetProperty("matchId").GetGuid(), f => f.GetProperty("code").GetString());
        Assert.Equal("Match.MakerChecker", failed[manual]);
        Assert.Equal(2, failed.Count);
    }

    [Fact]
    public async Task Manual_proposals_are_validated()
    {
        var s = await ReconScenario.CreateAsync(factory, _ct);
        var preparer = await s.Core.ClientAsAsync("pat", ["Preparer"], _ct);
        var unmatchedLine = s.LineIdByReference[AnswerKey.Items.First(i => i.Scenario == "UnmatchedLine").BankReferences[0]];
        var unpaid = s.ExpectedIdByExternalId[AnswerKey.Items.First(i => i.Scenario == "UnmatchedExpected").ExternalIds[0]];
        var autoMatched = AnswerKey.Items.First(i => i.Scenario == "CleanInvoice");
        var viewer = await s.Core.ClientAsAsync("vic", ["Viewer"], _ct);

        // Directions differ (POS outflow vs expected inflow).
        Assert.Equal(HttpStatusCode.BadRequest, (await preparer.PostAsJsonAsync("/api/recon/matches",
            new { statementLineIds = new[] { unmatchedLine }, expectedTransactionIds = new[] { unpaid } }, _ct)).StatusCode);

        // Already in a confirmed match.
        Assert.Equal(HttpStatusCode.Conflict, (await preparer.PostAsJsonAsync("/api/recon/matches", Body(s, autoMatched, null), _ct)).StatusCode);

        // Amounts too far apart: an ambiguous line against an unpaid invoice.
        var ambiguousLine = s.LineIdByReference[AnswerKey.Items.First(i => i.Scenario == "Ambiguous").BankReferences[0]];
        var unbalanced = await preparer.PostAsJsonAsync("/api/recon/matches",
            new { statementLineIds = new[] { ambiguousLine }, expectedTransactionIds = new[] { unpaid } }, _ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, unbalanced.StatusCode);
        Assert.Contains("Match.Unbalanced", await unbalanced.Content.ReadAsStringAsync(_ct), StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/recon/matches", Body(s, autoMatched, null), _ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await preparer.PostAsync($"/api/recon/matches/{Guid.NewGuid()}/confirm", null, _ct)).StatusCode);
    }

    [Fact]
    public async Task Exceptions_can_be_resolved_or_ignored_with_a_note()
    {
        var s = await ReconScenario.CreateAsync(factory, _ct);
        var open = await s.ExceptionsAsync();
        var (first, second) = (open[0], open[1]);

        Assert.Equal(HttpStatusCode.BadRequest, (await s.Core.Admin.PostAsJsonAsync($"/api/recon/exceptions/{first.Id}/resolve", new { note = "" }, _ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await s.Core.Admin.PostAsJsonAsync($"/api/recon/exceptions/{first.Id}/resolve", new { note = "Booked as bank charge" }, _ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await s.Core.Admin.PostAsJsonAsync($"/api/recon/exceptions/{second.Id}/ignore", new { note = "Internal transfer" }, _ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await s.Core.Admin.PostAsJsonAsync($"/api/recon/exceptions/{first.Id}/ignore", new { note = "again" }, _ct)).StatusCode);

        Assert.Contains(await s.ExceptionsAsync("Resolved"), e => e.Id == first.Id);
        Assert.Contains(await s.ExceptionsAsync("Ignored"), e => e.Id == second.Id);
    }

    [Fact]
    public async Task Summary_report_adds_up()
    {
        var s = await ReconScenario.CreateAsync(factory, _ct);
        var matches = await s.MatchesAsync();

        var summary = await s.Core.Admin.GetFromJsonAsync<SummaryBody>(
            $"/api/recon/reports/summary?accountId={s.Account}&from=2026-02-01&to=2026-02-28", CoreTestClient.Json, _ct);

        var lines = summary!.Lines;
        Assert.Equal(s.LineIdByReference.Count, lines.Total);
        Assert.Equal(lines.Total, lines.Matched + lines.Proposed + lines.Unmatched);
        Assert.Equal(matches.Where(m => m.Status == "Confirmed").SelectMany(m => m.Items).Count(i => i.ItemType == "StatementLine"), lines.Matched);
        Assert.Equal(matches.Count(m => m.Status == "Proposed"), summary.PendingProposals);
        Assert.Equal(lines.Unmatched, summary.UnmatchedLineAging.Sum(b => b.Count));
        Assert.Equal((await s.ExceptionsAsync()).Count, summary.OpenExceptionsByReason.Values.Sum());
        Assert.InRange(lines.MatchedPercentByCount, 1m, 100m);
    }

    [Theory]
    [InlineData("csv")]
    [InlineData("xlsx")]
    public async Task Export_lists_every_line_and_unmatched_expected_transaction(string format)
    {
        var s = await ReconScenario.CreateAsync(factory, _ct);

        var response = await s.Core.Admin.GetAsync($"/api/recon/reports/export?accountId={s.Account}&from=2026-02-01&to=2026-02-28&format={format}", _ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync(_ct);
        List<string[]> rows;
        if (format == "csv")
        {
            Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
            rows = System.Text.Encoding.UTF8.GetString(bytes).TrimStart('﻿').TrimEnd('\n').Split('\n').Skip(1).Select(r => r.Split(',')).ToList();
        }
        else
        {
            using var workbook = new XLWorkbook(new MemoryStream(bytes));
            var sheet = workbook.Worksheet(1);
            rows = sheet.RowsUsed().Skip(1).Select(r => new[] { r.Cell(1).GetString(), r.Cell(8).GetString() }.Concat(new string[6]).ToArray()).ToList();
        }

        var statementRows = rows.Count(r => r[0] == "StatementLine");
        Assert.Equal(s.LineIdByReference.Count, statementRows);
        Assert.Contains(rows, r => r[0] == "ExpectedTransaction");
        Assert.Contains(rows, r => r.Contains("Matched"));
        Assert.Contains(rows, r => r.Contains("Proposed"));
    }

    [Fact]
    public async Task Idempotency_key_replays_the_first_response()
    {
        var core = await CoreTestClient.CreateAsync(factory, _ct);
        var key = Guid.NewGuid().ToString();

        async Task<HttpResponseMessage> Post(object body)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/core/counterparties") { Content = JsonContent.Create(body) };
            request.Headers.Add("Idempotency-Key", key);
            return await core.Admin.SendAsync(request, _ct);
        }

        var first = await Post(new { name = "Idempotent Ltd" });
        var replay = await Post(new { name = "Idempotent Ltd" });
        var different = await Post(new { name = "Someone Else Ltd" });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal("true", replay.Headers.GetValues("Idempotent-Replayed").Single());
        Assert.Equal(await first.Content.ReadAsStringAsync(_ct), await replay.Content.ReadAsStringAsync(_ct));
        Assert.Equal(first.Headers.Location, replay.Headers.Location);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, different.StatusCode);
        Assert.Single((await core.Admin.GetFromJsonAsync<List<CounterpartyBody>>("/api/core/counterparties?search=idempotent", _ct))!);

        // Without the header, the same request is a normal duplicate.
        Assert.Equal(HttpStatusCode.Conflict, (await core.Admin.PostAsJsonAsync("/api/core/counterparties", new { name = "Idempotent Ltd" }, _ct)).StatusCode);
    }

    [Fact]
    public async Task Idempotency_does_not_store_server_errors_and_blocks_concurrent_repeats()
    {
        var tenantId = Guid.NewGuid();
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();

        HttpContext Request(string key)
        {
            var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            context.Request.Method = "POST";
            context.Request.Path = "/api/test";
            context.Request.Headers["Idempotency-Key"] = key;
            context.Response.Body = new MemoryStream();
            return context;
        }

        var failing = new IdempotencyMiddleware(ctx => { ctx.Response.StatusCode = 500; return Task.CompletedTask; });
        await failing.InvokeAsync(Request("k-500"), db, tenant, clock);
        Assert.False(await db.IdempotencyRecords.AnyAsync(r => r.TenantId == tenantId && r.Key == "k-500", _ct));

        var calls = 0;
        var succeeding = new IdempotencyMiddleware(ctx => { calls++; ctx.Response.StatusCode = 201; return Task.CompletedTask; });
        await succeeding.InvokeAsync(Request("k-500"), db, tenant, clock); // the retry runs for real
        await succeeding.InvokeAsync(Request("k-500"), db, tenant, clock); // and is then replayed
        Assert.Equal(1, calls);

        db.IdempotencyRecords.Add(new IdempotencyRecord { TenantId = tenantId, Key = "k-busy", RequestHash = await HashOfEmptyPostAsync(), CreatedAt = clock.UtcNow });
        await db.SaveChangesAsync(_ct);
        var busy = Request("k-busy");
        await succeeding.InvokeAsync(busy, db, tenant, clock);
        Assert.Equal(StatusCodes.Status409Conflict, busy.Response.StatusCode);
    }

    private static async Task<string> HashOfEmptyPostAsync()
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        return Convert.ToHexStringLower(await sha.ComputeHashAsync(new MemoryStream(System.Text.Encoding.UTF8.GetBytes("POST /api/test\n"))));
    }

    private static object Body(ReconScenario s, AnswerKey.Item item, string? reason) => new
    {
        statementLineIds = item.BankReferences.Select(r => s.LineIdByReference[r]),
        expectedTransactionIds = item.ExternalIds.Select(e => s.ExpectedIdByExternalId[e]),
        reason,
    };

    private sealed record CounterpartyBody(Guid Id, string Name);

    private sealed record AgingBody(string Bucket, int Count, decimal Amount);

    private sealed record LinesBody(int Total, int Matched, int Proposed, int Unmatched, decimal MatchedPercentByCount);

    private sealed record SummaryBody(LinesBody Lines, int PendingProposals, Dictionary<string, int> OpenExceptionsByReason, List<AgingBody> UnmatchedLineAging);
}
