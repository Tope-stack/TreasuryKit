namespace TreasuryKit.Api.Auth;

/// <summary>Decides whether the development sign-in endpoints (/dev/tenants, /dev/token) are mapped (ADRs 0003, 0010).</summary>
public static class DevLogin
{
    public const string SettingKey = "Auth:EnableDevLogin";

    /// <summary>
    /// On in Development; elsewhere only when <c>Auth:EnableDevLogin=true</c>. Refuses to start in Production with it enabled:
    /// the dev sign-in lets anyone act as any tenant and role.
    /// </summary>
    public static bool IsEnabled(IHostEnvironment environment, IConfiguration configuration)
    {
        if (environment.IsDevelopment())
        {
            return true;
        }

        var enabled = configuration.GetValue<bool>(SettingKey);
        if (enabled && environment.IsProduction())
        {
            throw new InvalidOperationException(
                $"{SettingKey} must not be enabled in Production. Use a dedicated environment (e.g. Demo) holding synthetic data only.");
        }

        return enabled;
    }
}
