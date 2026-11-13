using TreasuryKit.Core.Domain.Tenants;

namespace TreasuryKit.Core.Tests;

public sealed class TenantTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Creates_tenant_with_default_Lagos_time_zone()
    {
        var result = Tenant.Create("  Acme Nigeria Ltd ", null, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal("Acme Nigeria Ltd", result.Value.Name);
        Assert.Equal("Africa/Lagos", result.Value.BusinessTimeZone);
        Assert.Equal(Now, result.Value.CreatedAt);
        Assert.NotEqual(Guid.Empty, result.Value.Id);
    }

    [Fact]
    public void Accepts_explicit_time_zone() =>
        Assert.Equal("Africa/Nairobi", Tenant.Create("Acme Kenya", "Africa/Nairobi", Now).Value.BusinessTimeZone);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Requires_name(string? name) =>
        Assert.Equal(TenantErrors.NameRequired, Tenant.Create(name, null, Now).Error);

    [Fact]
    public void Rejects_overlong_name() =>
        Assert.Equal(TenantErrors.NameTooLong, Tenant.Create(new string('a', Tenant.NameMaxLength + 1), null, Now).Error);

    [Fact]
    public void Rejects_unknown_time_zone() =>
        Assert.Equal("Tenant.UnknownTimeZone", Tenant.Create("Acme", "Mars/Olympus", Now).Error.Code);
}
