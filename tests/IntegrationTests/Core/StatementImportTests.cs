using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TreasuryKit.Core.Contracts.Statements;
using TreasuryKit.Core.Infrastructure.Persistence;
using TreasuryKit.IntegrationTests.Infrastructure;

namespace TreasuryKit.IntegrationTests.Core;

/// <summary>Phase 1 acceptance criteria (plan §6) plus the import edge cases around them.</summary>
[Collection(ApiCollection.Name)]
public sealed class StatementImportTests(ApiFactory factory)
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Uploading_the_same_file_twice_creates_one_import()
    {
        var core = await CoreTestClient.CreateAsync(factory, _ct);
        var account = await core.CreateAccountAsync(CoreTestClient.LayoutA, _ct);

        var first = await CoreTestClient.UploadAsync(core.Admin, account, "layout-a/clean.csv", _ct);
        var second = await CoreTestClient.UploadAsync(core.Admin, account, "layout-a/clean.csv", _ct);

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var a = await first.Content.ReadFromJsonAsync<CoreTestClient.UploadBody>(_ct);
        var b = await second.Content.ReadFromJsonAsync<CoreTestClient.UploadBody>(_ct);
        Assert.Equal(a!.ImportId, b!.ImportId);
        Assert.True(b.AlreadyImported);

        await core.RunJobsAsync(_ct);
        Assert.Equal(1, await CountAsync(db => db.StatementImports.IgnoreQueryFilters().CountAsync(i => i.AccountId == account, _ct)));
        Assert.Equal(Manifest("layout-a/clean.csv").LineCount, (await core.GetLinesAsync(account, _ct)).TotalCount);
    }

    [Fact]
    public async Task Overlapping_statements_produce_no_duplicate_lines()
    {
        var core = await CoreTestClient.CreateAsync(factory, _ct);
        var account = await core.CreateAccountAsync(CoreTestClient.LayoutA, _ct);

        var part1 = await core.ImportAsync(account, "layout-a/overlap-part1.csv", _ct);
        var part2 = await core.ImportAsync(account, "layout-a/overlap-part2.csv", _ct);

        Assert.Equal("Completed", part1.Status);
        Assert.Equal("Completed", part2.Status);
        Assert.Equal(0, part1.DuplicateLineCount);
        Assert.Equal(Manifest("layout-a/overlap-part2.csv").OverlapWithPrevious, part2.DuplicateLineCount);

        var lines = await core.GetLinesAsync(account, _ct);
        Assert.Equal(Manifest("layout-a/clean.csv").LineCount, lines.TotalCount);
        Assert.Equal(2, lines.Items.Count(l => l.Narration == "SMS ALERT CHARGES")); // identical same-day charges both kept

        // The event reports only the newly inserted lines.
        var events = await EventsForAsync(part2.Id);
        Assert.Equal(part2.LineCount - part2.DuplicateLineCount, Assert.Single(events).NewLineCount);
    }

    [Fact]
    public async Task Unbalanced_file_ends_in_BalanceMismatch_until_an_admin_acknowledges_it()
    {
        var core = await CoreTestClient.CreateAsync(factory, _ct);
        var account = await core.CreateAccountAsync(CoreTestClient.LayoutA, _ct);

        var import = await core.ImportAsync(account, "layout-a/unbalanced.csv", _ct);

        Assert.Equal("BalanceMismatch", import.Status);
        Assert.Contains("Running balance breaks at row", import.Error, StringComparison.Ordinal);
        Assert.Equal(Manifest("layout-a/unbalanced.csv").LineCount, (await core.GetLinesAsync(account, _ct)).TotalCount); // lines kept
        Assert.Empty(await EventsForAsync(import.Id));

        var preparer = await core.ClientAsAsync("pat", ["Preparer"], _ct);
        var forbidden = await preparer.PostAsync($"/api/core/imports/{import.Id}/acknowledge-mismatch", null, _ct);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var acknowledged = await core.Admin.PostAsync($"/api/core/imports/{import.Id}/acknowledge-mismatch", null, _ct);
        Assert.Equal(HttpStatusCode.NoContent, acknowledged.StatusCode);

        var after = await core.GetImportAsync(import.Id, _ct);
        Assert.Equal("Completed", after.Status);
        Assert.Equal("admin-user", after.AcknowledgedBy);
        Assert.Single(await EventsForAsync(import.Id));
    }

    [Fact]
    public async Task Completed_import_writes_StatementImported_to_the_outbox()
    {
        var core = await CoreTestClient.CreateAsync(factory, _ct);
        var account = await core.CreateAccountAsync(CoreTestClient.LayoutA, _ct);
        var expected = Manifest("layout-a/clean.csv");

        var import = await core.ImportAsync(account, "layout-a/clean.csv", _ct);

        Assert.Equal("Completed", import.Status);
        Assert.True(import.BalanceValidated);
        Assert.Equal(expected.OpeningBalance, import.OpeningBalance);
        Assert.Equal(expected.ClosingBalance, import.ClosingBalance);

        var evt = Assert.Single(await EventsForAsync(import.Id));
        Assert.Equal(core.TenantId, evt.TenantId);
        Assert.Equal(account, evt.AccountId);
        Assert.Equal(expected.LineCount, evt.NewLineCount);
        Assert.Equal(expected.PeriodStart, evt.PeriodStart);
        Assert.Equal(expected.PeriodEnd, evt.PeriodEnd);
    }

    [Theory]
    [InlineData("layout-b/clean.xlsx", "Completed")]
    [InlineData("layout-b/unbalanced.xlsx", "BalanceMismatch")]
    public async Task Xlsx_statements_are_imported(string file, string status)
    {
        var core = await CoreTestClient.CreateAsync(factory, _ct);
        var account = await core.CreateAccountAsync(CoreTestClient.LayoutB, _ct);

        var import = await core.ImportAsync(account, file, _ct);

        Assert.Equal(status, import.Status);
        Assert.Equal(Manifest(file).LineCount, import.LineCount);
    }

    [Fact]
    public async Task Newest_first_statement_is_stored_oldest_first_and_validated()
    {
        var core = await CoreTestClient.CreateAsync(factory, _ct);
        var account = await core.CreateAccountAsync(CoreTestClient.LayoutC, _ct);

        var import = await core.ImportAsync(account, "layout-c/clean.csv", _ct);

        Assert.Equal("Completed", import.Status);
        Assert.True(import.BalanceValidated);
        var lines = await core.GetLinesAsync(account, _ct);
        Assert.Equal(Manifest("layout-c/clean.csv").PeriodStart, lines.Items[0].ValueDate);
    }

    [Fact]
    public async Task File_that_does_not_match_the_profile_fails_with_a_reason()
    {
        var core = await CoreTestClient.CreateAsync(factory, _ct);
        var account = await core.CreateAccountAsync(CoreTestClient.LayoutA, _ct);

        var import = await core.ImportAsync(account, "layout-c/clean.csv", _ct);

        Assert.Equal("Failed", import.Status);
        Assert.Contains("missing columns", import.Error, StringComparison.Ordinal);
        Assert.Equal(0, (await core.GetLinesAsync(account, _ct)).TotalCount);
    }

    [Theory]
    [InlineData(null, "layout-a/clean.csv", "Statement.NoMappingProfile")]
    [InlineData("a", "layout-b/clean.xlsx", "Statement.FormatMismatch")]
    [InlineData("a", "manifest.json", "Statement.UnsupportedFormat")]
    public async Task Upload_is_rejected_up_front_when_it_cannot_be_processed(string? profile, string file, string error)
    {
        var core = await CoreTestClient.CreateAsync(factory, _ct);
        var account = await core.CreateAccountAsync(profile is null ? null : CoreTestClient.LayoutA, _ct);

        var response = await CoreTestClient.UploadAsync(core.Admin, account, file, _ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(error, await response.Content.ReadAsStringAsync(_ct), StringComparison.Ordinal);
    }

    private static ManifestEntry Manifest(string file) => GoldenManifest.Entries[file];

    private async Task<List<StatementImported>> EventsForAsync(Guid importId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var messages = await scope.ServiceProvider.GetRequiredService<CoreDbContext>().OutboxMessages.AsNoTracking()
            .Where(m => m.Type.StartsWith(typeof(StatementImported).FullName!))
            .ToListAsync(_ct);
        return messages
            .Select(m => JsonSerializer.Deserialize<StatementImported>(m.Content, CoreTestClient.Json)!)
            .Where(e => e.ImportId == importId)
            .ToList();
    }

    private async Task<T> CountAsync<T>(Func<CoreDbContext, Task<T>> query)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<CoreDbContext>());
    }
}

internal sealed record ManifestEntry(
    int LineCount,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal OpeningBalance,
    decimal ClosingBalance,
    decimal SumOfAmounts,
    bool Balanced,
    int OverlapWithPrevious);

internal static class GoldenManifest
{
    public static readonly IReadOnlyDictionary<string, ManifestEntry> Entries =
        JsonSerializer.Deserialize<Dictionary<string, ManifestEntry>>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "statements", "manifest.json")),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
}
