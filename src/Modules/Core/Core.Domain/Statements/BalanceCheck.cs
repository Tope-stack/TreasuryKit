using System.Globalization;

namespace TreasuryKit.Core.Domain.Statements;

public enum BalanceCheckOutcome
{
    Balanced,
    Mismatch,
    NotValidated,
}

public sealed record BalanceCheck(BalanceCheckOutcome Outcome, string? Detail)
{
    public static readonly BalanceCheck Balanced = new(BalanceCheckOutcome.Balanced, null);

    public static readonly BalanceCheck NotValidated = new(BalanceCheckOutcome.NotValidated, null);

    /// <summary>
    /// Validates a statement in chronological order (plan §4.4): opening + Σ(lines) == closing, and, when the
    /// file has running balances, each balance equals the previous one plus the line amount.
    /// </summary>
    public static BalanceCheck Run(decimal? opening, decimal? closing, IReadOnlyList<(int RowNumber, decimal Amount, decimal? RunningBalance)> lines)
    {
        var validated = false;

        var previous = opening;
        foreach (var (row, amount, balance) in lines)
        {
            if (balance is null)
            {
                previous = null;
                continue;
            }

            if (previous is not null)
            {
                validated = true;
                if (previous.Value + amount != balance.Value)
                {
                    return Mismatch($"Running balance breaks at row {row}: {Format(previous.Value)} + {Format(amount)} ≠ {Format(balance.Value)}.");
                }
            }

            previous = balance;
        }

        if (opening is not null && closing is not null)
        {
            validated = true;
            var expected = opening.Value + lines.Sum(l => l.Amount);
            if (expected != closing.Value)
            {
                return Mismatch($"Opening {Format(opening.Value)} + lines {Format(expected - opening.Value)} = {Format(expected)}, but closing balance is {Format(closing.Value)}.");
            }
        }

        return validated ? Balanced : NotValidated;
    }

    private static BalanceCheck Mismatch(string detail) => new(BalanceCheckOutcome.Mismatch, detail);

    private static string Format(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);
}
