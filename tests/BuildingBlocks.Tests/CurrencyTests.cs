using TreasuryKit.BuildingBlocks.Domain;

namespace TreasuryKit.BuildingBlocks.Tests;

public sealed class CurrencyTests
{
    [Theory]
    [InlineData("NGN", 2)]
    [InlineData("usd", 2)]
    [InlineData(" GBP ", 2)]
    [InlineData("EUR", 2)]
    [InlineData("CNY", 2)]
    [InlineData("JPY", 0)]
    [InlineData("XOF", 0)]
    [InlineData("KWD", 3)]
    [InlineData("GHS", 2)]
    [InlineData("KES", 2)]
    [InlineData("ZAR", 2)]
    public void Resolves_known_codes_case_insensitively(string code, int minorUnits)
    {
        var result = Currency.FromCode(code);

        Assert.True(result.IsSuccess);
        Assert.Equal(code.Trim().ToUpperInvariant(), result.Value.Code);
        Assert.Equal(minorUnits, result.Value.MinorUnits);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("XXX")]
    [InlineData("NAIRA")]
    public void Rejects_unknown_codes(string? code)
    {
        var result = Currency.FromCode(code);

        Assert.True(result.IsFailure);
        Assert.Equal("Currency.Unknown", result.Error.Code);
    }

    [Fact]
    public void Same_code_is_same_instance_and_equal()
    {
        Assert.Same(Currency.NGN, Currency.FromCode("ngn").Value);
        Assert.True(Currency.USD == Currency.FromCode("USD").Value);
    }
}
