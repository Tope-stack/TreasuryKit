using System.Net;
using System.Net.Http.Json;
using TreasuryKit.IntegrationTests.Infrastructure;

namespace TreasuryKit.IntegrationTests.Core;

/// <summary>Role checks and tenant isolation on Core endpoints.</summary>
[Collection(ApiCollection.Name)]
public sealed class CoreAccessTests(ApiFactory factory)
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Other_tenants_cannot_see_accounts_imports_or_lines()
    {
        var owner = await CoreTestClient.CreateAsync(factory, _ct);
        var account = await owner.CreateAccountAsync(CoreTestClient.LayoutA, _ct);
        var import = await owner.ImportAsync(account, "layout-a/overlap-part1.csv", _ct);
        var intruder = await CoreTestClient.CreateAsync(factory, _ct);

        Assert.Equal(HttpStatusCode.NotFound, (await intruder.Admin.GetAsync($"/api/core/accounts/{account}", _ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.Admin.GetAsync($"/api/core/accounts/{account}/lines", _ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.Admin.GetAsync($"/api/core/imports/{import.Id}", _ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await CoreTestClient.UploadAsync(intruder.Admin, account, "layout-a/clean.csv", _ct)).StatusCode);
        Assert.Empty((await intruder.Admin.GetFromJsonAsync<List<CoreTestClient.IdResponse>>("/api/core/accounts", _ct))!);
    }

    [Fact]
    public async Task Roles_are_enforced()
    {
        var core = await CoreTestClient.CreateAsync(factory, _ct);
        var account = await core.CreateAccountAsync(CoreTestClient.LayoutA, _ct);
        var preparer = await core.ClientAsAsync("pat", ["Preparer"], _ct);
        var viewer = await core.ClientAsAsync("vic", ["Viewer"], _ct);

        var createByPreparer = await preparer.PostAsJsonAsync(
            "/api/core/accounts", new { bankCode = "058", bankName = "B", accountNumber = "999", currency = "NGN" }, _ct);
        Assert.Equal(HttpStatusCode.Forbidden, createByPreparer.StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await CoreTestClient.UploadAsync(viewer, account, "layout-a/clean.csv", _ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await CoreTestClient.UploadAsync(preparer, account, "layout-a/clean.csv", _ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"/api/core/accounts/{account}", _ct)).StatusCode);
    }

    [Fact]
    public async Task Duplicate_account_is_a_conflict()
    {
        var core = await CoreTestClient.CreateAsync(factory, _ct);
        var body = new { bankCode = "058", bankName = "Demo Bank", accountNumber = "5550001111", currency = "NGN" };

        Assert.Equal(HttpStatusCode.Created, (await core.Admin.PostAsJsonAsync("/api/core/accounts", body, _ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await core.Admin.PostAsJsonAsync("/api/core/accounts", body, _ct)).StatusCode);

        // The same account number is fine for another tenant.
        var other = await CoreTestClient.CreateAsync(factory, _ct);
        Assert.Equal(HttpStatusCode.Created, (await other.Admin.PostAsJsonAsync("/api/core/accounts", body, _ct)).StatusCode);
    }

    [Fact]
    public async Task Tenants_see_system_profiles_and_only_their_own_custom_profiles()
    {
        var first = await CoreTestClient.CreateAsync(factory, _ct);
        var second = await CoreTestClient.CreateAsync(factory, _ct);
        var profile = new
        {
            bankCode = "044",
            name = "My bank export",
            fileType = "Csv",
            headerRowIndex = 0,
            skipRows = 0,
            delimiter = ";",
            dateFormats = new[] { "yyyy-MM-dd" },
            culture = "en-NG",
            columnMap = new { bookingDate = new[] { "Date" }, narration = new[] { "Details" }, amount = new[] { "Amount" } },
            amountMode = "Signed",
        };

        var created = await first.Admin.PostAsJsonAsync("/api/core/mapping-profiles", profile, _ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var firstProfiles = await first.Admin.GetFromJsonAsync<List<ProfileBody>>("/api/core/mapping-profiles", CoreTestClient.Json, _ct);
        var secondProfiles = await second.Admin.GetFromJsonAsync<List<ProfileBody>>("/api/core/mapping-profiles", CoreTestClient.Json, _ct);

        Assert.Equal(3, firstProfiles!.Count(p => p.IsSystem));
        Assert.Contains(firstProfiles!, p => p.Name == "My bank export" && !p.IsSystem);
        Assert.DoesNotContain(secondProfiles!, p => p.Name == "My bank export");
    }

    [Fact]
    public async Task Counterparties_are_normalized_and_searchable()
    {
        var core = await CoreTestClient.CreateAsync(factory, _ct);

        var created = await core.Admin.PostAsJsonAsync(
            "/api/core/counterparties", new { name = "Lekki Pharma Ltd.", aliases = new[] { "LEKKI PHARMA" }, country = "NG" }, _ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var found = await core.Admin.GetFromJsonAsync<List<CounterpartyBody>>("/api/core/counterparties?search=lekki", _ct);
        var counterparty = Assert.Single(found!);
        Assert.Equal("LEKKI PHARMA LTD", counterparty.NormalizedName);
        Assert.Equal(["LEKKI PHARMA"], counterparty.Aliases);
    }

    private sealed record ProfileBody(Guid Id, bool IsSystem, string Name);

    private sealed record CounterpartyBody(Guid Id, string NormalizedName, List<string> Aliases);
}
