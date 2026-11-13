using TreasuryKit.BuildingBlocks.Domain;
using TreasuryKit.Recon.Domain.Matching;

namespace TreasuryKit.Recon.Domain.Exceptions;

public enum ExceptionStatus
{
    Open,
    Resolved,
    Ignored,

    /// <summary>Replaced by the exception from a later run (deviation from plan §5.1, which lists Open/Resolved/Ignored).</summary>
    Superseded,
}

/// <summary>An item a run could not match, with why and the closest candidates.</summary>
public sealed class ReconException : AggregateRoot<Guid>, ITenantOwned
{
    private ReconException(Guid id, Guid tenantId)
        : base(id) => TenantId = tenantId;

    private ReconException()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid AccountId { get; private set; }

    public Guid RunId { get; private set; }

    public ItemType ItemType { get; private set; }

    public Guid ItemId { get; private set; }

    public ExceptionReason Reason { get; private set; }

    public List<ExceptionCandidate> Candidates { get; private set; } = [];

    public ExceptionStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public string? ResolvedBy { get; private set; }

    public string? ResolutionNote { get; private set; }

    public static ReconException Raise(Guid tenantId, Guid accountId, Guid runId, UnmatchedItem item, DateTimeOffset now) =>
        new(Guid.CreateVersion7(), tenantId)
        {
            AccountId = accountId,
            RunId = runId,
            ItemType = item.Type,
            ItemId = item.Id,
            Reason = item.Reason,
            Candidates = item.Candidates.ToList(),
            Status = ExceptionStatus.Open,
            CreatedAt = now,
        };

    public void Supersede() => Status = ExceptionStatus.Superseded;
}
