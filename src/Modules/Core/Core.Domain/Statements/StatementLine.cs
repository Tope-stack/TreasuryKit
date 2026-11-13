using TreasuryKit.BuildingBlocks.Domain;

namespace TreasuryKit.Core.Domain.Statements;

/// <summary>One transaction from a bank statement. Written in bulk by the import pipeline; read-only afterwards.</summary>
public sealed class StatementLine : Entity<Guid>, ITenantOwned
{
    public StatementLine(
        Guid tenantId,
        Guid accountId,
        Guid importId,
        int rowNumber,
        DateOnly bookingDate,
        DateOnly valueDate,
        Money amount,
        string narration,
        string normalizedNarration,
        string? bankReference,
        string fingerprint,
        decimal? runningBalance)
        : base(Guid.CreateVersion7())
    {
        TenantId = tenantId;
        AccountId = accountId;
        ImportId = importId;
        RowNumber = rowNumber;
        BookingDate = bookingDate;
        ValueDate = valueDate;
        Amount = amount;
        Narration = narration;
        NormalizedNarration = normalizedNarration;
        BankReference = bankReference;
        Fingerprint = fingerprint;
        RunningBalance = runningBalance;
    }

    private StatementLine()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid AccountId { get; private set; }

    /// <summary>The import that first inserted this line.</summary>
    public Guid ImportId { get; private set; }

    /// <summary>1-based row in the source file, for traceability.</summary>
    public int RowNumber { get; private set; }

    public DateOnly BookingDate { get; private set; }

    public DateOnly ValueDate { get; private set; }

    /// <summary>Signed: credits positive, debits negative.</summary>
    public Money Amount { get; private set; } = null!;

    public string Narration { get; private set; } = null!;

    public string NormalizedNarration { get; private set; } = null!;

    public string? BankReference { get; private set; }

    /// <summary>JSON of references extracted from the narration (Phase 2): NIP session id, invoice and payment refs.</summary>
    public string ExtractedRefs { get; private set; } = "{}";

    public string? CounterpartyNameGuess { get; private set; }

    public string Fingerprint { get; private set; } = null!;

    public decimal? RunningBalance { get; private set; }
}
