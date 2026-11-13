using TreasuryKit.BuildingBlocks.Domain;
using TreasuryKit.Core.Domain.Statements.Narration;

namespace TreasuryKit.Core.Domain.ExpectedTransactions;

public enum TransactionDirection
{
    Inflow,
    Outflow,
}

public enum ExpectedTransactionSource
{
    Manual,
    Csv,
    Api,
}

public enum ExpectedTransactionStatus
{
    Open,
    PartiallySettled,
    Settled,
    Cancelled,
}

/// <summary>The editable fields of an expected transaction, validated together.</summary>
public sealed record ExpectedTransactionDetails(
    Guid? AccountId,
    TransactionDirection Direction,
    decimal Amount,
    Currency Currency,
    DateOnly ExpectedDate,
    Guid? CounterpartyId,
    string? CounterpartyName,
    string? Reference,
    string? Description);

/// <summary>A payable or receivable we expect to see on a bank statement (plan §4.1). Reused by CashForecast later.</summary>
public sealed class ExpectedTransaction : AggregateRoot<Guid>, ITenantOwned
{
    private ExpectedTransaction(Guid id, Guid tenantId)
        : base(id) => TenantId = tenantId;

    private ExpectedTransaction()
    {
    }

    public Guid TenantId { get; private set; }

    /// <summary>Null when it may settle through any account in its currency.</summary>
    public Guid? AccountId { get; private set; }

    public TransactionDirection Direction { get; private set; }

    /// <summary>Always positive; <see cref="Direction"/> carries the sign.</summary>
    public Money Amount { get; private set; } = null!;

    /// <summary>Amount not yet covered by confirmed matches, in <see cref="Amount"/>'s currency.</summary>
    public decimal OutstandingAmount { get; private set; }

    public DateOnly ExpectedDate { get; private set; }

    public Guid? CounterpartyId { get; private set; }

    public string? CounterpartyName { get; private set; }

    public string? Reference { get; private set; }

    /// <summary><see cref="Reference"/> in the canonical form used for matching (ADR 0006).</summary>
    public string? CanonicalReference { get; private set; }

    public string? Description { get; private set; }

    public ExpectedTransactionSource Source { get; private set; }

    /// <summary>The caller's id for this transaction; unique per tenant and source, making imports idempotent.</summary>
    public string? ExternalId { get; private set; }

    public ExpectedTransactionStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public static Result<ExpectedTransaction> Create(
        Guid tenantId,
        ExpectedTransactionDetails details,
        ExpectedTransactionSource source,
        string? externalId,
        DateTimeOffset now)
    {
        externalId = string.IsNullOrWhiteSpace(externalId) ? null : externalId.Trim();
        if (externalId is { Length: > 100 })
        {
            return Invalid("externalId must be at most 100 characters");
        }

        var transaction = new ExpectedTransaction(Guid.CreateVersion7(), tenantId)
        {
            Source = source,
            ExternalId = externalId,
            Status = ExpectedTransactionStatus.Open,
            CreatedAt = now,
        };

        var applied = transaction.Apply(details);
        return applied.IsFailure ? applied.Error : transaction;
    }

    /// <summary>True when <paramref name="details"/> would change nothing (used for idempotent re-imports).</summary>
    public bool Matches(ExpectedTransactionDetails details) =>
        AccountId == details.AccountId
        && Direction == details.Direction
        && Amount.Amount == details.Amount
        && Amount.Currency == details.Currency
        && ExpectedDate == details.ExpectedDate
        && CounterpartyId == details.CounterpartyId
        && CounterpartyName == Clean(details.CounterpartyName)
        && Reference == Clean(details.Reference)
        && Description == Clean(details.Description);

    /// <summary>Changes the details. Only open (untouched by matching) transactions can be edited.</summary>
    public Result Update(ExpectedTransactionDetails details, DateTimeOffset now)
    {
        if (Status != ExpectedTransactionStatus.Open)
        {
            return ExpectedTransactionErrors.NotEditable(Status);
        }

        var applied = Apply(details);
        if (applied.IsSuccess)
        {
            UpdatedAt = now;
        }

        return applied;
    }

    public Result Cancel(DateTimeOffset now)
    {
        if (Status is ExpectedTransactionStatus.Settled or ExpectedTransactionStatus.Cancelled)
        {
            return Error.Conflict("ExpectedTransaction.CannotCancel", $"A {Status} expected transaction cannot be cancelled.");
        }

        Status = ExpectedTransactionStatus.Cancelled;
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>A confirmed match covered <paramref name="amount"/>. Returns the amount actually applied.</summary>
    public decimal ApplyAllocation(decimal amount, DateTimeOffset now)
    {
        var applied = Math.Min(Math.Abs(amount), OutstandingAmount);
        OutstandingAmount -= applied;
        if (Status != ExpectedTransactionStatus.Cancelled)
        {
            Status = OutstandingAmount == 0m ? ExpectedTransactionStatus.Settled : ExpectedTransactionStatus.PartiallySettled;
        }

        UpdatedAt = now;
        return applied;
    }

    /// <summary>A confirmed match was reversed: restore <paramref name="amount"/> to the outstanding balance.</summary>
    public void ReverseAllocation(decimal amount, DateTimeOffset now)
    {
        OutstandingAmount = Math.Min(Amount.Amount, OutstandingAmount + Math.Abs(amount));
        if (Status != ExpectedTransactionStatus.Cancelled)
        {
            Status = OutstandingAmount == Amount.Amount ? ExpectedTransactionStatus.Open : ExpectedTransactionStatus.PartiallySettled;
        }

        UpdatedAt = now;
    }

    private Result Apply(ExpectedTransactionDetails d)
    {
        var problems = new List<string>();
        if (d.Amount <= 0m)
        {
            problems.Add("amount must be positive");
        }

        if (decimal.Round(d.Amount, d.Currency.MinorUnits) != d.Amount)
        {
            problems.Add($"amount has more than {d.Currency.MinorUnits} decimal places for {d.Currency.Code}");
        }

        if (Clean(d.CounterpartyName) is { Length: > 200 })
        {
            problems.Add("counterpartyName must be at most 200 characters");
        }

        if (Clean(d.Reference) is { Length: > 100 })
        {
            problems.Add("reference must be at most 100 characters");
        }

        if (Clean(d.Description) is { Length: > 500 })
        {
            problems.Add("description must be at most 500 characters");
        }

        if (problems.Count > 0)
        {
            return Invalid(string.Join("; ", problems));
        }

        AccountId = d.AccountId;
        Direction = d.Direction;
        Amount = new Money(d.Amount, d.Currency);
        OutstandingAmount = d.Amount;
        ExpectedDate = d.ExpectedDate;
        CounterpartyId = d.CounterpartyId;
        CounterpartyName = Clean(d.CounterpartyName);
        Reference = Clean(d.Reference);
        CanonicalReference = Reference is null ? null : ExtractedReferences.Canonical(Reference) is { Length: > 0 } c ? c : null;
        Description = Clean(d.Description);
        return Result.Success();
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Error Invalid(string reason) => Error.Validation("ExpectedTransaction.Invalid", reason + ".");
}

public static class ExpectedTransactionErrors
{
    public static readonly Error NotFound = Error.NotFound("ExpectedTransaction.NotFound", "Expected transaction not found.");

    public static Error NotEditable(ExpectedTransactionStatus status) =>
        Error.Conflict("ExpectedTransaction.NotEditable", $"Only open expected transactions can be edited (status is {status}).");
}
