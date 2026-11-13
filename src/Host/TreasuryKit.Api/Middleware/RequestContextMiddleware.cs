using TreasuryKit.BuildingBlocks.Infrastructure.Authorization;
using TreasuryKit.BuildingBlocks.Infrastructure.Context;

namespace TreasuryKit.Api.Middleware;

/// <summary>Copies the authenticated user's tenant, id and roles into the scoped request contexts.</summary>
internal sealed class RequestContextMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context, TenantContext tenant, UserContext user)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            if (Guid.TryParse(context.User.FindFirst(TreasuryClaimTypes.Tenant)?.Value, out var tenantId))
            {
                tenant.Set(tenantId);
            }

            user.Set(
                context.User.FindFirst(TreasuryClaimTypes.Subject)?.Value,
                context.User.FindAll(TreasuryClaimTypes.Role).Select(c => c.Value));
        }

        return next(context);
    }
}
