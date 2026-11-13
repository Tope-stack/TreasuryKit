using TreasuryKit.BuildingBlocks.Domain;

namespace TreasuryKit.Core.Domain.Statements;

/// <summary>One uploaded statement file and the outcome of processing it.</summary>
public sealed class StatementImport : AggregateRoot<Guid>, ITenantOwned
{
    private StatementImport(Guid id, Guid tenantId)
        : base(id) => TenantId = tenantId;

    private StatementImport()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid AccountId { get; private set; }

    public string FileName { get; private set; } = null!;

    /// <summary>Lower-case hex SHA-256 of the file. Unique per account: re-uploads return this import.</summary>
    public string FileHash { get; private set; } = null!;

    /// <summary>Key of the stored file in the file store.</summary>
    public string FileKey { get; private set; } = null!;

    public StatementFormat Format { get; private set; }

    public Currency Currency { get; private set; } = null!;

    public DateOnly? PeriodStart { get; private set; }

    public DateOnly? PeriodEnd { get; private set; }

    /// <summary>Opening balance in <see cref="Currency"/>, when the file states or implies one.</summary>
    public decimal? OpeningBalance { get; private set; }

    /// <summary>Closing balance in <see cref="Currency"/>, when the file states or implies one.</summary>
    public decimal? ClosingBalance { get; private set; }

    /// <summary>Lines in the file.</summary>
    public int LineCount { get; private set; }

    /// <summary>Lines already present from an earlier import (overlapping periods); not inserted again.</summary>
    public int DuplicateLineCount { get; private set; }

    /// <summary>Entries in the file that were not imported because they are not booked (CAMT pending/info entries).</summary>
    public int SkippedEntryCount { get; private set; }

    /// <summary>False when the file carried no balances to validate against.</summary>
    public bool BalanceValidated { get; private set; }

    public StatementImportStatus Status { get; private set; }

    public string? Error { get; private set; }

    public string ImportedBy { get; private set; } = null!;

    public DateTimeOffset ImportedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public string? AcknowledgedBy { get; private set; }

    public DateTimeOffset? AcknowledgedAt { get; private set; }

    public static StatementImport Queue(
        Guid tenantId,
        Guid accountId,
        Currency currency,
        string fileName,
        string fileHash,
        string fileKey,
        StatementFormat format,
        string importedBy,
        DateTimeOffset now) =>
        new(Guid.CreateVersion7(), tenantId)
        {
            AccountId = accountId,
            Currency = currency,
            FileName = fileName,
            FileHash = fileHash,
            FileKey = fileKey,
            Format = format,
            ImportedBy = importedBy,
            ImportedAt = now,
            Status = StatementImportStatus.Queued,
        };

    /// <summary>True when processing should (re)start: queued, or interrupted mid-parse.</summary>
    public bool IsPending => Status is StatementImportStatus.Queued or StatementImportStatus.Parsing;

    public void StartParsing() => EnsurePending().Status = StatementImportStatus.Parsing;

    public void Fail(string error, DateTimeOffset now)
    {
        EnsurePending();
        Status = StatementImportStatus.Failed;
        Error = error.Length > 2000 ? error[..2000] : error;
        CompletedAt = now;
    }

    /// <summary>Records the parse/insert outcome. Ends Completed, or BalanceMismatch when validation failed.</summary>
    public void Complete(
        DateOnly? periodStart,
        DateOnly? periodEnd,
        decimal? openingBalance,
        decimal? closingBalance,
        int lineCount,
        int duplicateLineCount,
        BalanceCheck balanceCheck,
        DateTimeOffset now,
        int skippedEntryCount = 0)
    {
        EnsurePending();
        SkippedEntryCount = skippedEntryCount;
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
        OpeningBalance = openingBalance;
        ClosingBalance = closingBalance;
        LineCount = lineCount;
        DuplicateLineCount = duplicateLineCount;
        BalanceValidated = balanceCheck.Outcome != BalanceCheckOutcome.NotValidated;
        CompletedAt = now;

        if (balanceCheck.Outcome == BalanceCheckOutcome.Mismatch)
        {
            Status = StatementImportStatus.BalanceMismatch;
            Error = balanceCheck.Detail;
        }
        else
        {
            Status = StatementImportStatus.Completed;
        }
    }

    /// <summary>An Admin accepts a balance mismatch; the import's lines become available for reconciliation.</summary>
    public Result AcknowledgeMismatch(string userId, DateTimeOffset now)
    {
        if (Status != StatementImportStatus.BalanceMismatch)
        {
            return BuildingBlocks.Domain.Error.Conflict(
                "StatementImport.NotMismatched", $"Only imports in BalanceMismatch can be acknowledged (status is {Status}).");
        }

        Status = StatementImportStatus.Completed;
        AcknowledgedBy = userId;
        AcknowledgedAt = now;
        return Result.Success();
    }

    private StatementImport EnsurePending() =>
        IsPending ? this : throw new InvalidOperationException($"Import {Id} is already {Status}.");
}

public static class StatementImportErrors
{
    public static readonly Error NotFound = BuildingBlocks.Domain.Error.NotFound("StatementImport.NotFound", "Statement import not found.");
}
