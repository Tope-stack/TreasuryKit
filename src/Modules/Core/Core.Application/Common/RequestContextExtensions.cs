using TreasuryKit.BuildingBlocks.Application.Abstractions;

namespace TreasuryKit.Core.Application.Common;

internal static class RequestContextExtensions
{
    /// <summary>The current tenant; endpoints require a tenant claim, so absence is a programming error.</summary>
    public static Guid Required(this ICurrentTenant tenant) =>
        tenant.TenantId ?? throw new InvalidOperationException("No current tenant.");

    public static string Required(this ICurrentUser user) =>
        user.UserId ?? throw new InvalidOperationException("No current user.");
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
