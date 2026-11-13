using TreasuryKit.BuildingBlocks.Domain;

namespace TreasuryKit.Core.Domain.ExpectedTransactions;

public enum ExpectedTransactionImportStatus
{
    Queued,
    Processing,
    Completed,
    Failed,
}

public sealed record ImportRowError(int Row, string Message);

/// <summary>One uploaded expected-transactions CSV and its outcome.</summary>
public sealed class ExpectedTransactionImport : AggregateRoot<Guid>, ITenantOwned
{
    private ExpectedTransactionImport(Guid id, Guid tenantId)
        : base(id) => TenantId = tenantId;

    private ExpectedTransactionImport()
    {
    }

    public Guid TenantId { get; private set; }

    public string FileName { get; private set; } = null!;

    public string FileHash { get; private set; } = null!;

    public string FileKey { get; private set; } = null!;

    public ExpectedTransactionImportStatus Status { get; private set; }

    public int Created { get; private set; }

    public int Updated { get; private set; }

    /// <summary>Rows identical to an existing transaction (re-import).</summary>
    public int Unchanged { get; private set; }

    /// <summary>Rows not applied because the existing transaction can no longer change (settled, cancelled).</summary>
    public int Skipped { get; private set; }

    public List<ImportRowError> Errors { get; private set; } = [];

    public string ImportedBy { get; private set; } = null!;

    public DateTimeOffset ImportedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public static ExpectedTransactionImport Queue(
        Guid tenantId, string fileName, string fileHash, string fileKey, string importedBy, DateTimeOffset now) =>
        new(Guid.CreateVersion7(), tenantId)
        {
            FileName = fileName,
            FileHash = fileHash,
            FileKey = fileKey,
            ImportedBy = importedBy,
            ImportedAt = now,
            Status = ExpectedTransactionImportStatus.Queued,
        };

    public bool IsPending => Status is ExpectedTransactionImportStatus.Queued or ExpectedTransactionImportStatus.Processing;

    public void StartProcessing() => Status = ExpectedTransactionImportStatus.Processing;

    public void Complete(int created, int updated, int unchanged, int skipped, IReadOnlyList<ImportRowError> errors, DateTimeOffset now)
    {
        Created = created;
        Updated = updated;
        Unchanged = unchanged;
        Skipped = skipped;
        Errors = errors.Take(500).ToList();
        Status = ExpectedTransactionImportStatus.Completed;
        CompletedAt = now;
    }

    public void Fail(string error, DateTimeOffset now)
    {
        Errors = [new ImportRowError(0, error.Length > 2000 ? error[..2000] : error)];
        Status = ExpectedTransactionImportStatus.Failed;
        CompletedAt = now;
    }
}
