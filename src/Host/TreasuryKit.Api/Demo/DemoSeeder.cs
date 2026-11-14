using TreasuryKit.BuildingBlocks.Application.Messaging;
using TreasuryKit.BuildingBlocks.Domain;
using TreasuryKit.BuildingBlocks.Infrastructure.Authorization;
using TreasuryKit.BuildingBlocks.Infrastructure.Context;
using TreasuryKit.Core.Application.Accounts;
using TreasuryKit.Core.Application.ExpectedTransactions;
using TreasuryKit.Core.Application.Statements;
using TreasuryKit.Core.Application.Tenants;

namespace TreasuryKit.Api.Demo;

/// <summary>
/// Gives an empty demo database a ready-to-show tenant (ADR 0012): one bank account and February 2026's synthetic
/// expected transactions and statement, uploaded through the normal commands so the background jobs import and
/// reconcile them exactly as in the tests. Does nothing when any tenant exists, so it never touches real data.
/// </summary>
public static class DemoSeeder
{
    public const string SettingKey = "Demo:SeedOnStartup";
    public const string TenantName = "Demo Trading Ltd";
    public const string AccountNumber = "0123456789";
    public const string SeedUser = "demo-seed";

    /// <summary>The system mapping profile "Generic layout A", seeded by Core's migrations.</summary>
    private static readonly Guid LayoutAProfileId = Guid.Parse("5e3d6a10-0000-4000-8000-00000000000a");

    /// <summary>Returns the new tenant's id, or null when the database already had tenants.</summary>
    public static async Task<Guid?> SeedIfEmptyAsync(IServiceProvider services, ILogger logger, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        var existing = Unwrap(await dispatcher.QueryAsync(new ListTenantsQuery(), cancellationToken), "list tenants");
        if (existing.Count > 0)
        {
            logger.LogInformation("Demo seed skipped: the database already has {Count} tenant(s).", existing.Count);
            return null;
        }

        try
        {
            var tenantId = Unwrap(await dispatcher.SendAsync(new CreateTenantCommand(TenantName, null), cancellationToken), "create tenant");

            // Act as an Admin of the new tenant for the remaining commands.
            scope.ServiceProvider.GetRequiredService<TenantContext>().Set(tenantId);
            scope.ServiceProvider.GetRequiredService<UserContext>().Set(SeedUser, [Roles.Admin]);

            var accountId = Unwrap(
                await dispatcher.SendAsync(
                    new CreateBankAccountCommand("058", "Demo Bank", AccountNumber, "NGN", null, LayoutAProfileId), cancellationToken),
                "create account");

            // Expected transactions first: their import job runs before the statement's, so the automatic run sees them.
            await using (var expected = Open("expected.csv"))
            {
                Unwrap(await dispatcher.SendAsync(new UploadExpectedTransactionsCommand("expected.csv", expected), cancellationToken), "upload expected transactions");
            }

            await using (var statement = Open("statement.csv"))
            {
                Unwrap(await dispatcher.SendAsync(new UploadStatementCommand(accountId, "statement.csv", statement), cancellationToken), "upload statement");
            }

            logger.LogInformation(
                "Demo seed: created tenant '{Tenant}' with account {Account} and queued February 2026's expected transactions and statement.",
                TenantName, AccountNumber);
            return tenantId;
        }
        catch (Exception ex) when (ex is InvalidOperationException)
        {
            // A failed seed must not take the demo down; the site still works without sample data.
            logger.LogError(ex, "Demo seed failed");
            return null;
        }
    }

    private static Stream Open(string file) =>
        typeof(DemoSeeder).Assembly.GetManifestResourceStream($"DemoData.{file}")
        ?? throw new InvalidOperationException($"Embedded demo file '{file}' is missing.");

    private static T Unwrap<T>(Result<T> result, string step) =>
        result.IsSuccess ? result.Value : throw new InvalidOperationException($"Demo seed could not {step}: {result.Error.Message}");
}
