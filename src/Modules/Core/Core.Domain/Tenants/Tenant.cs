using TreasuryKit.BuildingBlocks.Domain;

namespace TreasuryKit.Core.Domain.Tenants;

/// <summary>An organisation using TreasuryKit. Every tenant-owned row in every schema carries its id.</summary>
public sealed class Tenant : AggregateRoot<Guid>
{
    public const string DefaultBusinessTimeZone = "Africa/Lagos";
    public const int NameMaxLength = 200;

    private Tenant(Guid id, string name, string businessTimeZone, DateTimeOffset createdAt)
        : base(id)
    {
        Name = name;
        BusinessTimeZone = businessTimeZone;
        CreatedAt = createdAt;
    }

    /// <summary>For EF Core materialization only.</summary>
    private Tenant()
    {
    }

    public string Name { get; private set; } = null!;

    /// <summary>IANA time zone used to interpret business dates, e.g. "Africa/Lagos".</summary>
    public string BusinessTimeZone { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public static Result<Tenant> Create(string? name, string? businessTimeZone, DateTimeOffset now)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return TenantErrors.NameRequired;
        }

        if (name.Length > NameMaxLength)
        {
            return TenantErrors.NameTooLong;
        }

        var timeZone = string.IsNullOrWhiteSpace(businessTimeZone) ? DefaultBusinessTimeZone : businessTimeZone.Trim();
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out _))
        {
            return TenantErrors.UnknownTimeZone(timeZone);
        }

        return new Tenant(Guid.CreateVersion7(), name, timeZone, now);
    }
}

public static class TenantErrors
{
    public static readonly Error NameRequired = Error.Validation("Tenant.NameRequired", "Tenant name is required.");

    public static readonly Error NameTooLong =
        Error.Validation("Tenant.NameTooLong", $"Tenant name must be at most {Tenant.NameMaxLength} characters.");

    public static Error UnknownTimeZone(string timeZone) =>
        Error.Validation("Tenant.UnknownTimeZone", $"'{timeZone}' is not a known IANA time zone.");
}
