using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TreasuryKit.Core.Infrastructure.Persistence;
using TreasuryKit.IntegrationTests.Infrastructure;

namespace TreasuryKit.IntegrationTests.Core;

/// <summary>Phase 2: MT940 and CAMT.053 imports, account checks, and reference extraction end to end.</summary>
[Collection(ApiCollection.Name)]
public sealed class StructuredStatementTests(ApiFactory factory)
{
    private const string AccountNumber = "0123456789";

    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Mt940_is_imported_without_a_mapping_profile_and_references_are_extracted()
    {
        var core = await CoreTestClient.CreateAsync(factory, _ct);
        var account = await core.CreateAccountAsync(null, _ct, AccountNumber);

        var import = await core.ImportAsync(account, "mt940/month.sta", _ct);

        Assert.Equal("Completed", import.Status);
        Assert.True(import.BalanceValidated);
        Assert.Equal(GoldenManifest.Entries["mt940/month.sta"].LineCount, import.LineCount);

        var lines = await LinesAsync(account);
        Assert.Equal(GoldenManifest.Entries["mt940/month.sta"].NipSessionIds, await CountNipSessionIdsAsync(account));
        Assert.Contains(lines, l => l.ExtractedRefs.Contains("INV1041", StringComparison.Ordinal) && l.CounterpartyNameGuess == "JOS TIN MERCHANTS");
        Assert.DoesNotContain(lines, l => l.NormalizedNarration.StartsWith("NIP", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Camt_after_csv_for_the_same_month_adds_no_lines()
    {
        var core = await CoreTestClient.CreateAsync(factory, _ct);
        var account = await core.CreateAccountAsync(CoreTestClient.LayoutA, _ct, AccountNumber);

        var csv = await core.ImportAsync(account, "layout-a/clean.csv", _ct);
        var camt = await core.ImportAsync(account, "camt053/month.xml", _ct);

        Assert.Equal("Completed", csv.Status);
        Assert.Equal("Completed", camt.Status);
        Assert.Equal(camt.LineCount, camt.DuplicateLineCount);
        Assert.Equal(csv.LineCount, (await core.GetLinesAsync(account, _ct)).TotalCount);
    }

    [Fact]
    public async Task Statement_for_another_account_fails()
    {
        var core = await CoreTestClient.CreateAsync(factory, _ct);
        var account = await core.CreateAccountAsync(null, _ct, AccountNumber);

        var import = await core.ImportAsync(account, "mt940/other-account.sta", _ct);

        Assert.Equal("Failed", import.Status);
        Assert.Contains("9876543210", import.Error, StringComparison.Ordinal);
        Assert.Equal(0, (await core.GetLinesAsync(account, _ct)).TotalCount);
    }

    [Theory]
    [InlineData("mt940/daily.sta", "Completed")]
    [InlineData("mt940/daily-gap.sta", "BalanceMismatch")]
    public async Task Multi_statement_mt940_checks_continuity(string file, string status)
    {
        var core = await CoreTestClient.CreateAsync(factory, _ct);
        var account = await core.CreateAccountAsync(null, _ct, AccountNumber);

        var import = await core.ImportAsync(account, file, _ct);

        Assert.Equal(status, import.Status);
        Assert.Equal(GoldenManifest.Entries[file].LineCount, import.LineCount);
    }

    [Fact]
    public async Task Camt_pending_entries_are_skipped_and_counted()
    {
        var core = await CoreTestClient.CreateAsync(factory, _ct);
        var account = await core.CreateAccountAsync(null, _ct, AccountNumber);

        var import = await core.ImportAsync(account, "camt053/batch-pending-v02.xml", _ct);

        Assert.Equal("Completed", import.Status);
        Assert.Equal(2, import.LineCount);
        Assert.Equal(1, import.SkippedEntryCount);
        var credit = (await LinesAsync(account)).Single(l => l.Amount.Amount > 0);
        Assert.Equal("OKONKWO AND SONS", credit.CounterpartyNameGuess);
        Assert.Contains("000013260202091500123456789012", credit.ExtractedRefs, StringComparison.Ordinal); // end-to-end id
    }

    [Fact]
    public async Task Changing_invoice_patterns_re_extracts_existing_lines()
    {
        var core = await CoreTestClient.CreateAsync(factory, _ct);
        var account = await core.CreateAccountAsync(null, _ct, AccountNumber);
        await core.ImportAsync(account, "mt940/month.sta", _ct);
        Assert.DoesNotContain(await LinesAsync(account), l => l.ExtractedRefs.Contains("\"PO311\"", StringComparison.Ordinal));

        var defaults = await core.Admin.GetFromJsonAsync<PatternsBody>("/api/core/settings/reference-patterns", _ct);
        Assert.Equal([@"INV[-/ ]?\d+"], defaults!.InvoicePatterns);

        var invalid = await core.Admin.PutAsJsonAsync("/api/core/settings/reference-patterns", new { invoicePatterns = new[] { "(oops" } }, _ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var preparer = await core.ClientAsAsync("pat", ["Preparer"], _ct);
        var forbidden = await preparer.PutAsJsonAsync("/api/core/settings/reference-patterns", new { invoicePatterns = new[] { "X" } }, _ct);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var updated = await core.Admin.PutAsJsonAsync(
            "/api/core/settings/reference-patterns", new { invoicePatterns = new[] { @"INV[-/ ]?\d+", @"PO-\d+" } }, _ct);
        Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);
        await core.RunJobsAsync(_ct);

        Assert.Contains(await LinesAsync(account), l => l.ExtractedRefs.Contains("\"PO311\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Maintenance_reextract_backfills_lines()
    {
        var core = await CoreTestClient.CreateAsync(factory, _ct);
        var account = await core.CreateAccountAsync(null, _ct, AccountNumber);
        await core.ImportAsync(account, "mt940/month.sta", _ct);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            await db.Database.ExecuteSqlAsync(
                $"UPDATE core.statement_lines SET extracted_refs = '{{}}'::jsonb, counterparty_name_guess = NULL WHERE account_id = {account}", _ct);
        }

        var response = await core.Admin.PostAsync("/api/core/maintenance/reextract", null, _ct);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await core.RunJobsAsync(_ct);

        var lines = await LinesAsync(account);
        Assert.Equal(GoldenManifest.Entries["mt940/month.sta"].NipSessionIds, await CountNipSessionIdsAsync(account));
    }

    [Fact]
    public async Task Mt940_with_txt_extension_is_detected_by_content()
    {
        var core = await CoreTestClient.CreateAsync(factory, _ct);
        var account = await core.CreateAccountAsync(null, _ct, AccountNumber);
        using var form = new MultipartFormDataContent();
        var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "TestData", "statements", "mt940", "daily.sta"), _ct);
        form.Add(new ByteArrayContent(bytes), "file", "export.txt");

        var response = await core.Admin.PostAsync($"/api/core/accounts/{account}/statements", form, _ct);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    private async Task<List<global::TreasuryKit.Core.Domain.Statements.StatementLine>> LinesAsync(Guid accountId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<CoreDbContext>().StatementLines
            .IgnoreQueryFilters().AsNoTracking().Where(l => l.AccountId == accountId).ToListAsync(_ct);
    }

    private async Task<int> CountNipSessionIdsAsync(Guid accountId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<CoreDbContext>().Database
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM core.statement_lines WHERE account_id = {accountId} AND extracted_refs->>'nipSessionId' IS NOT NULL")
            .SingleAsync(_ct);
    }

    private sealed record PatternsBody(List<string> InvoicePatterns);
}
