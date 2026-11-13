using System.Globalization;

namespace TreasuryKit.BuildingBlocks.Domain;

/// <summary>
/// A signed monetary amount in a single currency. Arithmetic across currencies is a programming error and throws.
/// </summary>
public sealed class Money : ValueObject
{
    public Money(decimal amount, Currency currency)
    {
        ArgumentNullException.ThrowIfNull(currency);
        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }

    public Currency Currency { get; }

    public bool IsZero => Amount == 0m;

    public bool IsPositive => Amount > 0m;

    public bool IsNegative => Amount < 0m;

    public static Money Zero(Currency currency) => new(0m, currency);

    public static Money operator +(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount + right.Amount, left.Currency);
    }

    public static Money operator -(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount - right.Amount, left.Currency);
    }

    public static Money operator -(Money money) => new(-money.Amount, money.Currency);

    public static bool operator >(Money left, Money right) => Compare(left, right) > 0;

    public static bool operator <(Money left, Money right) => Compare(left, right) < 0;

    public static bool operator >=(Money left, Money right) => Compare(left, right) >= 0;

    public static bool operator <=(Money left, Money right) => Compare(left, right) <= 0;

    public Money Abs() => new(Math.Abs(Amount), Currency);

    /// <summary>Rounds to the currency's minor units, half away from zero.</summary>
    public Money RoundToMinorUnits() =>
        new(Math.Round(Amount, Currency.MinorUnits, MidpointRounding.AwayFromZero), Currency);

    public override string ToString() =>
        $"{Currency.Code} {Amount.ToString("N" + Currency.MinorUnits, CultureInfo.InvariantCulture)}";

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }

    private static int Compare(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return left.Amount.CompareTo(right.Amount);
    }

    private static void EnsureSameCurrency(Money left, Money right)
    {
        if (left.Currency != right.Currency)
        {
            throw new InvalidOperationException(
                $"Cannot combine {left.Currency.Code} and {right.Currency.Code} amounts.");
        }
    }
}
