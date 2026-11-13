using TreasuryKit.BuildingBlocks.Domain;

namespace TreasuryKit.Recon.Domain.Runs;

public enum RunStatus
{
    Queued,
    Running,
    Completed,
    Failed,
}

public enum RunTrigger
{
    Event,
    User,
}

/// <summary>Counts recorded on a completed run (plan §5.1 Stats).</summary>
public sealed record RunStats(
    int LinesConsidered,
    int ExpectedConsidered,
    int AutoConfirmed,
    int Proposed,
    int Exceptions,
    int Superseded,
    long DurationMs);

/// <summary>One execution of the matching pipeline for an account and period. At most one is Queued or Running per account.</summary>
public sealed class ReconciliationRun : AggregateRoot<Guid>, ITenantOwned
{
    public const int MaxPeriodDays = 366;

    private ReconciliationRun(Guid id, Guid tenantId)
        : base(id) => TenantId = tenantId;

    private ReconciliationRun()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid AccountId { get; private set; }

    public DateOnly PeriodStart { get; private set; }

    public DateOnly PeriodEnd { get; private set; }

    public RunStatus Status { get; private set; }

    public RunTrigger TriggeredBy { get; private set; }

    public string RequestedBy { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public RunStats? Stats { get; private set; }

    public string? Error { get; private set; }

    /// <summary>Another trigger arrived while this run was active; a follow-up run covers <see cref="RerunStart"/>–<see cref="RerunEnd"/>.</summary>
    public bool RerunRequested { get; private set; }

    public DateOnly? RerunStart { get; private set; }

    public DateOnly? RerunEnd { get; private set; }

    public bool IsActive => Status is RunStatus.Queued or RunStatus.Running;

    public static Result<ReconciliationRun> Queue(
        Guid tenantId, Guid accountId, DateOnly from, DateOnly to, RunTrigger trigger, string requestedBy, DateTimeOffset now)
    {
        if (to < from)
        {
            return BuildingBlocks.Domain.Error.Validation("Run.InvalidPeriod", "'to' must not be before 'from'.");
        }

        if (to.DayNumber - from.DayNumber + 1 > MaxPeriodDays)
        {
            return BuildingBlocks.Domain.Error.Validation("Run.InvalidPeriod", $"A run covers at most {MaxPeriodDays} days.");
        }

        return new ReconciliationRun(Guid.CreateVersion7(), tenantId)
        {
            AccountId = accountId,
            PeriodStart = from,
            PeriodEnd = to,
            TriggeredBy = trigger,
            RequestedBy = requestedBy,
            CreatedAt = now,
            Status = RunStatus.Queued,
        };
    }

    public void Start(DateTimeOffset now)
    {
        Status = RunStatus.Running;
        StartedAt = now;
    }

    public void Complete(RunStats stats, DateTimeOffset now)
    {
        Status = RunStatus.Completed;
        Stats = stats;
        CompletedAt = now;
    }

    public void Fail(string error, DateTimeOffset now)
    {
        Status = RunStatus.Failed;
        Error = error.Length > 2000 ? error[..2000] : error;
        CompletedAt = now;
    }

    /// <summary>Remembers that another period needs matching once this run finishes.</summary>
    public void RequestRerun(DateOnly from, DateOnly to)
    {
        RerunRequested = true;
        RerunStart = RerunStart is { } start && start < from ? start : from;
        RerunEnd = RerunEnd is { } end && end > to ? end : to;
    }
}

public static class RunErrors
{
    public static readonly Error NotFound = BuildingBlocks.Domain.Error.NotFound("Run.NotFound", "Reconciliation run not found.");

    public static Error AlreadyActive(Guid activeRunId) =>
        BuildingBlocks.Domain.Error.Conflict("Run.AlreadyActive", $"A reconciliation run is already queued or running for this account (run {activeRunId}).");

    /// <summary>A business-rule refusal (422), not malformed input.</summary>
    public static readonly Error Blocked = new(
        "Run.Blocked",
        "A statement import in this period has a balance mismatch that an Admin has not acknowledged.",
        ErrorType.Failure);
}
