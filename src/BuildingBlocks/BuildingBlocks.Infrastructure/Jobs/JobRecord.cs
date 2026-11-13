using System.Text.Json;
using TreasuryKit.BuildingBlocks.Application.Jobs;
using TreasuryKit.BuildingBlocks.Infrastructure.Auditing;
using TreasuryKit.BuildingBlocks.Infrastructure.Persistence;

namespace TreasuryKit.BuildingBlocks.Infrastructure.Jobs;

/// <summary>A row in a module's <c>jobs</c> table.</summary>
public sealed class JobRecord : INotAudited
{
    public const int DefaultMaxAttempts = 5;

    private JobRecord()
    {
    }

    public Guid Id { get; private set; }

    public string Type { get; private set; } = null!;

    public string Payload { get; private set; } = null!;

    public Guid? TenantId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset RunAfter { get; private set; }

    public int Attempts { get; private set; }

    public int MaxAttempts { get; private set; }

    /// <summary>Lease: while in the future, another worker holds the job.</summary>
    public DateTimeOffset? LockedUntil { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public DateTimeOffset? FailedAt { get; private set; }

    public string? LastError { get; private set; }

    public static JobRecord Create(IJob job, DateTimeOffset now)
    {
        var type = job.GetType();
        return new JobRecord
        {
            Id = Guid.CreateVersion7(),
            Type = IntegrationEventSerializer.TypeName(type),
            Payload = JsonSerializer.Serialize(job, type, IntegrationEventSerializer.Options),
            TenantId = job.TenantId,
            CreatedAt = now,
            RunAfter = now,
            MaxAttempts = DefaultMaxAttempts,
        };
    }

    public void MarkCompleted(DateTimeOffset now)
    {
        CompletedAt = now;
        LockedUntil = null;
        LastError = null;
    }

    /// <summary>Schedules a retry with exponential backoff, or fails permanently. Returns true when permanent.</summary>
    public bool MarkFailed(string error, DateTimeOffset now)
    {
        LastError = error.Length > 4000 ? error[..4000] : error;
        LockedUntil = null;
        if (Attempts >= MaxAttempts)
        {
            FailedAt = now;
            return true;
        }

        RunAfter = now + TimeSpan.FromSeconds(5 * Math.Pow(2, Attempts - 1));
        return false;
    }
}
