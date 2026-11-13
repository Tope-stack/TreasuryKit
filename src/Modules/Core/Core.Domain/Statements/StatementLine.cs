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
        decimal? runningBalance,
        string extractedRefs,
        string? counterpartyNameGuess,
        string? endToEndId,
        string? counterpartyName)
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
        ExtractedRefs = extractedRefs;
        CounterpartyNameGuess = counterpartyNameGuess;
        EndToEndId = endToEndId;
        CounterpartyName = counterpartyName;
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

    /// <summary>JSON <see cref="Narration.ExtractedReferences"/>: NIP session id, invoice refs (canonical), payment refs.</summary>
    public string ExtractedRefs { get; private set; } = "{}";

    /// <summary>Guessed from the narration, or taken from <see cref="CounterpartyName"/> when the format provides one.</summary>
    public string? CounterpartyNameGuess { get; private set; }

    /// <summary>End-to-end payment id from structured formats (CAMT.053 <c>EndToEndId</c>).</summary>
    public string? EndToEndId { get; private set; }

    /// <summary>Debtor/creditor name from structured formats (CAMT.053 related parties).</summary>
    public string? CounterpartyName { get; private set; }

    public string Fingerprint { get; private set; } = null!;

    public decimal? RunningBalance { get; private set; }
}
