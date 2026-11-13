using TreasuryKit.BuildingBlocks.Domain;
using TreasuryKit.Core.Domain.Common;

namespace TreasuryKit.Core.Domain.Counterparties;

/// <summary>A customer or supplier. Normalized name and aliases feed counterparty matching in reconciliation.</summary>
public sealed class Counterparty : AggregateRoot<Guid>, ITenantOwned
{
    private Counterparty(Guid id, Guid tenantId)
        : base(id) => TenantId = tenantId;

    private Counterparty()
    {
    }

    public Guid TenantId { get; private set; }

    public string Name { get; private set; } = null!;

    public string NormalizedName { get; private set; } = null!;

    /// <summary>Normalized alternative names, e.g. how the counterparty appears in bank narrations.</summary>
    public List<string> Aliases { get; private set; } = [];

    /// <summary>ISO 3166-1 alpha-2 country code.</summary>
    public string? Country { get; private set; }

    public Currency? DefaultCurrency { get; private set; }

    public static Result<Counterparty> Create(
        Guid tenantId,
        string? name,
        IEnumerable<string>? aliases,
        string? country,
        string? defaultCurrencyCode)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 200)
        {
            return Error.Validation("Counterparty.Invalid", "name is required (max 200 characters).");
        }

        country = string.IsNullOrWhiteSpace(country) ? null : country.Trim().ToUpperInvariant();
        if (country is not null && (country.Length != 2 || !country.All(char.IsAsciiLetterUpper)))
        {
            return Error.Validation("Counterparty.Invalid", "country must be an ISO 3166-1 alpha-2 code.");
        }

        Currency? currency = null;
        if (!string.IsNullOrWhiteSpace(defaultCurrencyCode))
        {
            var parsed = Currency.FromCode(defaultCurrencyCode);
            if (parsed.IsFailure)
            {
                return parsed.Error;
            }

            currency = parsed.Value;
        }

        var normalizedName = TextNormalization.ForName(name);
        return new Counterparty(Guid.CreateVersion7(), tenantId)
        {
            Name = name,
            NormalizedName = normalizedName,
            Aliases = (aliases ?? [])
                .Select(TextNormalization.ForName)
                .Where(a => a.Length > 0 && a != normalizedName)
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            Country = country,
            DefaultCurrency = currency,
        };
    }
}
