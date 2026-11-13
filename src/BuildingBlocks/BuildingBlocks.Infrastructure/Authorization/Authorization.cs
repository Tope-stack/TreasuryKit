using Microsoft.AspNetCore.Authorization;

namespace TreasuryKit.BuildingBlocks.Infrastructure.Authorization;

public static class Roles
{
    public const string Admin = "Admin";
    public const string Preparer = "Preparer";
    public const string Approver = "Approver";
    public const string Viewer = "Viewer";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal) { Admin, Preparer, Approver, Viewer };
}

public static class TreasuryClaimTypes
{
    public const string Subject = "sub";
    public const string Tenant = "tenant_id";
    public const string Role = "role";
}

public static class Policies
{
    /// <summary>Any authenticated tenant user.</summary>
    public const string CanView = nameof(CanView);

    /// <summary>Preparers (makers) and admins.</summary>
    public const string CanPrepare = nameof(CanPrepare);

    /// <summary>Approvers (checkers) and admins. Maker-checker separation is enforced by the command, not the policy.</summary>
    public const string CanApprove = nameof(CanApprove);

    public const string AdminOnly = nameof(AdminOnly);

    public static void AddTreasuryPolicies(this AuthorizationOptions options)
    {
        AddPolicy(options, CanView, Roles.Admin, Roles.Preparer, Roles.Approver, Roles.Viewer);
        AddPolicy(options, CanPrepare, Roles.Admin, Roles.Preparer);
        AddPolicy(options, CanApprove, Roles.Admin, Roles.Approver);
        AddPolicy(options, AdminOnly, Roles.Admin);
    }

    private static void AddPolicy(AuthorizationOptions options, string name, params string[] roles) =>
        options.AddPolicy(name, policy => policy
            .RequireAuthenticatedUser()
            .RequireClaim(TreasuryClaimTypes.Tenant)
            .RequireRole(roles));
}
