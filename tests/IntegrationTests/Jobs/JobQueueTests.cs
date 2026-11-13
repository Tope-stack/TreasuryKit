using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TreasuryKit.BuildingBlocks.Application.Abstractions;
using TreasuryKit.BuildingBlocks.Application.Jobs;
using TreasuryKit.BuildingBlocks.Infrastructure.Jobs;
using TreasuryKit.Core.Infrastructure.Persistence;
using TreasuryKit.IntegrationTests.Infrastructure;

namespace TreasuryKit.IntegrationTests.Jobs;

public sealed record TestJob(Guid? TenantId, Guid Key, int FailTimes) : IJob;

public sealed class JobCallLog
{
    public ConcurrentDictionary<Guid, int> Attempts { get; } = new();

    public ConcurrentDictionary<Guid, Guid?> TenantSeen { get; } = new();

    public ConcurrentDictionary<Guid, string> PermanentFailures { get; } = new();
}

public sealed class TestJobHandler(JobCallLog log, ICurrentTenant tenant) : IJobHandler<TestJob>
{
    public Task HandleAsync(TestJob job, CancellationToken cancellationToken)
    {
        var attempt = log.Attempts.AddOrUpdate(job.Key, 1, (_, n) => n + 1);
        log.TenantSeen[job.Key] = tenant.TenantId;
        return attempt <= job.FailTimes ? throw new InvalidOperationException($"Planned failure {attempt}") : Task.CompletedTask;
    }

    public Task OnFailedPermanentlyAsync(TestJob job, string error, CancellationToken cancellationToken)
    {
        log.PermanentFailures[job.Key] = error;
        return Task.CompletedTask;
    }
}

[Collection(ApiCollection.Name)]
public sealed class JobQueueTests(ApiFactory factory)
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private JobCallLog Log => factory.Services.GetRequiredService<JobCallLog>();

    private JobDispatcher Dispatcher => factory.Services.GetRequiredService<JobDispatcher>();

    [Fact]
    public async Task Job_runs_with_its_tenant_and_completes()
    {
        var job = new TestJob(Guid.CreateVersion7(), Guid.NewGuid(), FailTimes: 0);
        var id = await EnqueueAsync(job);

        await Dispatcher.RunDueJobsAsync(_ct);

        Assert.Equal(1, Log.Attempts[job.Key]);
        Assert.Equal(job.TenantId, Log.TenantSeen[job.Key]);
        var record = await GetAsync(id);
        Assert.NotNull(record.CompletedAt);
        Assert.Null(record.LockedUntil);
    }

    [Fact]
    public async Task Failed_job_is_retried_after_backoff()
    {
        var job = new TestJob(null, Guid.NewGuid(), FailTimes: 1);
        var id = await EnqueueAsync(job);

        await Dispatcher.RunDueJobsAsync(_ct);
        var afterFailure = await GetAsync(id);
        Assert.Null(afterFailure.CompletedAt);
        Assert.Equal(1, afterFailure.Attempts);
        Assert.True(afterFailure.RunAfter > DateTimeOffset.UtcNow, "retry must be scheduled in the future");

        await Dispatcher.RunDueJobsAsync(_ct);
        Assert.Equal(1, Log.Attempts[job.Key]); // not due yet

        await MakeDueAsync(id);
        await Dispatcher.RunDueJobsAsync(_ct);

        Assert.Equal(2, Log.Attempts[job.Key]);
        Assert.NotNull((await GetAsync(id)).CompletedAt);
    }

    [Fact]
    public async Task Job_fails_permanently_after_max_attempts()
    {
        var job = new TestJob(null, Guid.NewGuid(), FailTimes: int.MaxValue);
        var id = await EnqueueAsync(job);

        for (var i = 0; i < JobRecord.DefaultMaxAttempts; i++)
        {
            await MakeDueAsync(id);
            await Dispatcher.RunDueJobsAsync(_ct);
        }

        var record = await GetAsync(id);
        Assert.NotNull(record.FailedAt);
        Assert.Equal(JobRecord.DefaultMaxAttempts, record.Attempts);
        Assert.Contains("Planned failure", Log.PermanentFailures[job.Key], StringComparison.Ordinal);

        await MakeDueAsync(id);
        await Dispatcher.RunDueJobsAsync(_ct);
        Assert.Equal(JobRecord.DefaultMaxAttempts, Log.Attempts[job.Key]); // never picked up again
    }

    [Fact]
    public async Task Leased_job_is_not_picked_up_by_another_worker()
    {
        var job = new TestJob(null, Guid.NewGuid(), FailTimes: 0);
        var id = await EnqueueAsync(job);
        await ExecuteAsync(db => db.Database.ExecuteSqlAsync(
            $"UPDATE core.jobs SET locked_until = {DateTimeOffset.UtcNow.AddMinutes(5)} WHERE id = {id}", _ct));

        await Dispatcher.RunDueJobsAsync(_ct);

        Assert.False(Log.Attempts.ContainsKey(job.Key));
    }

    private async Task<Guid> EnqueueAsync(TestJob job)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        db.Enqueue(job);
        await db.SaveChangesAsync(_ct);
        return db.ChangeTracker.Entries<JobRecord>().Single().Entity.Id;
    }

    private Task MakeDueAsync(Guid id) =>
        ExecuteAsync(db => db.Database.ExecuteSqlAsync($"UPDATE core.jobs SET run_after = {DateTimeOffset.UtcNow.AddMinutes(-1)} WHERE id = {id}", _ct));

    private async Task<JobRecord> GetAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<CoreDbContext>().Jobs.AsNoTracking().SingleAsync(j => j.Id == id, _ct);
    }

    private async Task ExecuteAsync(Func<CoreDbContext, Task> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<CoreDbContext>());
    }
}
