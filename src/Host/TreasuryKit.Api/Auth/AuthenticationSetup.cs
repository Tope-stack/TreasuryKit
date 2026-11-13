using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TreasuryKit.BuildingBlocks.Infrastructure.Authorization;

namespace TreasuryKit.Api.Auth;

internal static class AuthenticationSetup
{
    public static IServiceCollection AddTreasuryAuthentication(this IServiceCollection services)
    {
        services.AddOptions<JwtOptions>()
            .BindConfiguration(JwtOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
            {
                // Keep short JWT claim names ("sub", "role", "tenant_id") as-is.
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Value.Issuer,
                    ValidAudience = jwt.Value.Audience,
                    IssuerSigningKey = jwt.Value.GetSigningKey(),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    NameClaimType = TreasuryClaimTypes.Subject,
                    RoleClaimType = TreasuryClaimTypes.Role,
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        return services;
    }
}
