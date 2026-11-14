using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace TreasuryKit.Api.OpenApi;

/// <summary>The OpenAPI document (/openapi/v1.json) and the Scalar API reference (/scalar).</summary>
public static class ApiReference
{
    public const string SettingKey = "Api:EnableReference";

    /// <summary>On in Development, and wherever <c>Api:EnableReference=true</c> (the Demo environment turns it on).</summary>
    public static bool IsEnabled(IHostEnvironment environment, IConfiguration configuration) =>
        environment.IsDevelopment() || configuration.GetValue<bool>(SettingKey);

    /// <summary>Declares JWT bearer authentication so the reference can send a token with "Try it" requests.</summary>
    public static OpenApiOptions AddBearerSecurity(this OpenApiOptions options) =>
        options.AddDocumentTransformer((document, _, _) =>
        {
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "A token from POST /dev/token (development and demo environments).",
            };
            document.Security ??= [];
            document.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] });
            return Task.CompletedTask;
        });
}
