using TreasuryKit.Core.Domain.Statements;

namespace TreasuryKit.Core.Tests.Statements;

public sealed class BalanceCheckTests
{
    [Fact]
    public void Balanced_when_opening_plus_lines_equals_closing() =>
        Assert.Equal(BalanceCheck.Balanced, BalanceCheck.Run(100m, 130m, [(2, 50m, null), (3, -20m, null)]));

    [Fact]
    public void Mismatch_when_totals_disagree()
    {
        var check = BalanceCheck.Run(100m, 131m, [(2, 50m, null), (3, -20m, null)]);

        Assert.Equal(BalanceCheckOutcome.Mismatch, check.Outcome);
        Assert.Contains("closing balance is 131.00", check.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Mismatch_names_the_row_where_running_balance_breaks()
    {
        var check = BalanceCheck.Run(100m, 130m, [(2, 50m, 150m), (3, -20m, 131m)]);

        Assert.Equal(BalanceCheckOutcome.Mismatch, check.Outcome);
        Assert.Contains("row 3", check.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Not_validated_without_any_balances() =>
        Assert.Equal(BalanceCheckOutcome.NotValidated, BalanceCheck.Run(null, null, [(2, 50m, null)]).Outcome);

    [Fact]
    public void Running_balances_alone_validate_the_chain() =>
        Assert.Equal(BalanceCheckOutcome.Balanced, BalanceCheck.Run(null, null, [(2, 50m, 150m), (3, -20m, 130m)]).Outcome);
}
