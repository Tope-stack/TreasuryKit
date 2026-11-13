using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TreasuryKit.Api.Auth;
using TreasuryKit.BuildingBlocks.Application.Abstractions;
using TreasuryKit.BuildingBlocks.Application.Messaging;
using TreasuryKit.BuildingBlocks.Infrastructure.Authorization;
using TreasuryKit.BuildingBlocks.Infrastructure.Http;
using TreasuryKit.Core.Application.Tenants;

namespace TreasuryKit.Api.Endpoints;

/// <summary>Development-only helpers: create a tenant and mint a token for it. Never mapped outside Development.</summary>
internal static class DevEndpoints
{
    public static void MapDevEndpoints(this IEndpointRouteBuilder app)
    {
        var dev = app.MapGroup("/dev").AllowAnonymous().WithTags("Dev");

        dev.MapPost("/tenants", async (CreateTenantRequest request, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.SendAsync(new CreateTenantCommand(request.Name, request.BusinessTimeZone), ct))
                .Match(id => Results.Created($"/dev/tenants/{id}", new { tenantId = id })));

        dev.MapPost("/token", (DevTokenRequest request, IOptions<JwtOptions> options, IClock clock) =>
        {
            var unknown = request.Roles.Where(r => !Roles.All.Contains(r)).ToList();
            if (request.TenantId == Guid.Empty || string.IsNullOrWhiteSpace(request.UserId) || request.Roles.Length == 0 || unknown.Count > 0)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["request"] = [$"tenantId, userId and at least one role are required. Valid roles: {string.Join(", ", Roles.All)}."],
                });
            }

            var jwt = options.Value;
            var expires = clock.UtcNow.AddMinutes(jwt.AccessTokenLifetimeMinutes);
            var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Issuer = jwt.Issuer,
                Audience = jwt.Audience,
                IssuedAt = clock.UtcNow.UtcDateTime,
                NotBefore = clock.UtcNow.UtcDateTime,
                Expires = expires.UtcDateTime,
                Claims = new Dictionary<string, object>
                {
                    [TreasuryClaimTypes.Subject] = request.UserId,
                    [TreasuryClaimTypes.Tenant] = request.TenantId.ToString(),
                    [TreasuryClaimTypes.Role] = request.Roles,
                },
                SigningCredentials = new SigningCredentials(jwt.GetSigningKey(), SecurityAlgorithms.HmacSha256),
            });

            return Results.Ok(new { accessToken = token, expiresAt = expires });
        });
    }

    internal sealed record CreateTenantRequest(string Name, string? BusinessTimeZone);

    internal sealed record DevTokenRequest(Guid TenantId, string UserId, string[] Roles);
}
