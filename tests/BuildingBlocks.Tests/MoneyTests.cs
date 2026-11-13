using TreasuryKit.BuildingBlocks.Domain;

namespace TreasuryKit.BuildingBlocks.Tests;

public sealed class MoneyTests
{
    [Fact]
    public void Adds_and_subtracts_same_currency()
    {
        var a = new Money(1_000.50m, Currency.NGN);
        var b = new Money(250.25m, Currency.NGN);

        Assert.Equal(new Money(1_250.75m, Currency.NGN), a + b);
        Assert.Equal(new Money(750.25m, Currency.NGN), a - b);
        Assert.Equal(new Money(-1_000.50m, Currency.NGN), -a);
    }

    [Fact]
    public void Mixing_currencies_throws()
    {
        var naira = new Money(100m, Currency.NGN);
        var dollars = new Money(100m, Currency.USD);

        Assert.Throws<InvalidOperationException>(() => naira + dollars);
        Assert.Throws<InvalidOperationException>(() => naira > dollars);
    }

    [Fact]
    public void Equality_is_by_amount_and_currency()
    {
        Assert.Equal(new Money(10m, Currency.USD), new Money(10.00m, Currency.USD));
        Assert.NotEqual(new Money(10m, Currency.USD), new Money(10m, Currency.EUR));
        Assert.Equal(new Money(10m, Currency.USD).GetHashCode(), new Money(10.00m, Currency.USD).GetHashCode());
    }

    [Theory]
    [InlineData("NGN", "10.005", "10.01")]
    [InlineData("NGN", "-10.005", "-10.01")]
    [InlineData("JPY", "100.5", "101")]
    [InlineData("KWD", "1.23456", "1.235")]
    public void Rounds_to_minor_units_half_away_from_zero(string code, string amount, string expected)
    {
        var money = new Money(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), Currency.FromCode(code).Value);

        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), money.RoundToMinorUnits().Amount);
    }

    [Fact]
    public void Formats_with_currency_precision() =>
        Assert.Equal("NGN 1,234,567.50", new Money(1_234_567.5m, Currency.NGN).ToString());
}
