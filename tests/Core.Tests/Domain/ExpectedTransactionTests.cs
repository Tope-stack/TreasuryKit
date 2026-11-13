using TreasuryKit.BuildingBlocks.Domain;
using TreasuryKit.Core.Domain.ExpectedTransactions;

namespace TreasuryKit.Core.Tests.Domain;

public sealed class ExpectedTransactionTests
{
    private static readonly DateTimeOffset Now = new(2026, 2, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Creates_open_with_full_outstanding_and_canonical_reference()
    {
        var t = Create(Details(1_000m, reference: " inv-1041 "));

        Assert.Equal(ExpectedTransactionStatus.Open, t.Status);
        Assert.Equal(1_000m, t.OutstandingAmount);
        Assert.Equal("inv-1041", t.Reference);
        Assert.Equal("INV1041", t.CanonicalReference);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(10.005)]
    public void Rejects_invalid_amounts(double amount) =>
        Assert.True(ExpectedTransaction.Create(Guid.NewGuid(), Details((decimal)amount), ExpectedTransactionSource.Manual, null, Now).IsFailure);

    [Fact]
    public void Allocations_settle_partially_then_fully_and_reversal_restores()
    {
        var t = Create(Details(1_000m));

        t.ApplyAllocation(400m, Now);
        Assert.Equal(ExpectedTransactionStatus.PartiallySettled, t.Status);
        Assert.Equal(600m, t.OutstandingAmount);

        Assert.Equal(600m, t.ApplyAllocation(900m, Now)); // never below zero
        Assert.Equal(ExpectedTransactionStatus.Settled, t.Status);

        t.ReverseAllocation(600m, Now);
        Assert.Equal(ExpectedTransactionStatus.PartiallySettled, t.Status);
        t.ReverseAllocation(400m, Now);
        Assert.Equal(ExpectedTransactionStatus.Open, t.Status);
        Assert.Equal(1_000m, t.OutstandingAmount);
    }

    [Fact]
    public void Only_open_transactions_can_be_edited()
    {
        var t = Create(Details(1_000m));
        Assert.True(t.Update(Details(1_200m), Now).IsSuccess);
        Assert.Equal(1_200m, t.OutstandingAmount);

        t.ApplyAllocation(100m, Now);
        Assert.Equal("ExpectedTransaction.NotEditable", t.Update(Details(1_300m), Now).Error.Code);
    }

    [Fact]
    public void Matches_detects_unchanged_details() =>
        Assert.True(Create(Details(10m, reference: "R1")).Matches(Details(10m, reference: " R1 ")));

    [Fact]
    public void Settled_cannot_be_cancelled()
    {
        var t = Create(Details(10m));
        t.ApplyAllocation(10m, Now);

        Assert.True(t.Cancel(Now).IsFailure);
    }

    private static ExpectedTransaction Create(ExpectedTransactionDetails details) =>
        ExpectedTransaction.Create(Guid.NewGuid(), details, ExpectedTransactionSource.Manual, null, Now).Value;

    private static ExpectedTransactionDetails Details(decimal amount, string? reference = null) =>
        new(null, TransactionDirection.Inflow, amount, Currency.NGN, new DateOnly(2026, 2, 10), null, "Eko Foods", reference, null);
}
