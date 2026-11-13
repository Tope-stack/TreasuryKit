using TreasuryKit.BuildingBlocks.Application.Abstractions;
using TreasuryKit.BuildingBlocks.Infrastructure.Authorization;

namespace TreasuryKit.Api.Endpoints;

internal static class MeEndpoint
{
    /// <summary>Echoes the caller's identity as the API sees it — handy for checking tokens.</summary>
    public static void MapMeEndpoint(this IEndpointRouteBuilder app) =>
        app.MapGet("/api/me", (ICurrentUser user, ICurrentTenant tenant) =>
                Results.Ok(new { userId = user.UserId, tenantId = tenant.TenantId, roles = user.Roles.Order() }))
            .RequireAuthorization(Policies.CanView);
}
