using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace TreasuryKit.Api.Middleware;

public sealed class DemoGateOptions
{
    public const string SectionName = "DemoGate";

    public string Username { get; set; } = "demo";

    /// <summary>When empty, the gate is off. Set it (as a secret) on hosts without a gating proxy, e.g. Render (ADR 0011).</summary>
    public string? Password { get; set; }

    public bool Enabled => !string.IsNullOrEmpty(Password);
}

/// <summary>
/// The demo's shared-password gate, in-process (the Caddy gate's twin, ADR 0010): HTTP basic auth for every request without
/// a bearer token. API calls carry their own bearer token, validated by the Api; tokens can only come from /dev/token,
/// which is behind the gate. /health stays open for the platform's health checks.
/// </summary>
public sealed class DemoGateMiddleware(RequestDelegate next, IOptions<DemoGateOptions> options)
{
    private readonly byte[] _expected = Encoding.UTF8.GetBytes($"{options.Value.Username}:{options.Value.Password}");

    public Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/health") || HasBearer(context.Request) || HasValidBasic(context.Request))
        {
            return next(context);
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = "Basic realm=\"TreasuryKit demo\", charset=\"UTF-8\"";
        return Task.CompletedTask;
    }

    private static bool HasBearer(HttpRequest request) =>
        request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);

    private bool HasValidBasic(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        if (!header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var supplied = Convert.FromBase64String(header["Basic ".Length..].Trim());
            return CryptographicOperations.FixedTimeEquals(supplied, _expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
