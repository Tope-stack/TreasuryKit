using System.Collections.Frozen;

namespace TreasuryKit.BuildingBlocks.Domain;

/// <summary>An ISO 4217 currency with its minor-unit precision.</summary>
public sealed class Currency : ValueObject
{
    // Active ISO 4217 currencies. Minor units default to 2; exceptions listed explicitly.
    private const string ActiveCodes =
        "AED AFN ALL AMD AOA ARS AUD AWG AZN BAM BBD BDT BGN BHD BIF BMD BND BOB BRL BSD BTN BWP BYN BZD " +
        "CAD CDF CHF CLF CLP CNY COP CRC CUP CVE CZK DJF DKK DOP DZD EGP ERN ETB EUR FJD FKP GBP GEL GHS " +
        "GIP GMD GNF GTQ GYD HKD HNL HTG HUF IDR ILS INR IQD IRR ISK JMD JOD JPY KES KGS KHR KMF KPW KRW " +
        "KWD KYD KZT LAK LBP LKR LRD LSL LYD MAD MDL MGA MKD MMK MNT MOP MRU MUR MVR MWK MXN MYR MZN NAD " +
        "NGN NIO NOK NPR NZD OMR PAB PEN PGK PHP PKR PLN PYG QAR RON RSD RUB RWF SAR SBD SCR SDG SEK SGD " +
        "SHP SLE SOS SRD SSP STN SVC SYP SZL THB TJS TMT TND TOP TRY TTD TWD TZS UAH UGX USD UYU UZS VED " +
        "VES VND VUV WST XAF XCD XCG XOF XPF YER ZAR ZMW ZWG";

    private static readonly FrozenDictionary<string, int> MinorUnitExceptions = new Dictionary<string, int>
    {
        ["BIF"] = 0, ["CLP"] = 0, ["DJF"] = 0, ["GNF"] = 0, ["ISK"] = 0, ["JPY"] = 0, ["KMF"] = 0,
        ["KRW"] = 0, ["PYG"] = 0, ["RWF"] = 0, ["UGX"] = 0, ["VND"] = 0, ["VUV"] = 0, ["XAF"] = 0,
        ["XOF"] = 0, ["XPF"] = 0,
        ["BHD"] = 3, ["IQD"] = 3, ["JOD"] = 3, ["KWD"] = 3, ["LYD"] = 3, ["OMR"] = 3, ["TND"] = 3,
        ["CLF"] = 4,
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<string, Currency> ByCode = ActiveCodes
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Select(code => new Currency(code, MinorUnitExceptions.GetValueOrDefault(code, 2)))
        .ToFrozenDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase);

    public static readonly Currency NGN = ByCode["NGN"];
    public static readonly Currency USD = ByCode["USD"];
    public static readonly Currency GBP = ByCode["GBP"];
    public static readonly Currency EUR = ByCode["EUR"];
    public static readonly Currency CNY = ByCode["CNY"];

    private Currency(string code, int minorUnits)
    {
        Code = code;
        MinorUnits = minorUnits;
    }

    public string Code { get; }

    public int MinorUnits { get; }

    public static IReadOnlyCollection<Currency> All => ByCode.Values;

    public static Result<Currency> FromCode(string? code) =>
        code is not null && ByCode.TryGetValue(code.Trim(), out var currency)
            ? currency
            : Error.Validation("Currency.Unknown", $"'{code}' is not a supported ISO 4217 currency code.");

    public override string ToString() => Code;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Code;
    }
}
