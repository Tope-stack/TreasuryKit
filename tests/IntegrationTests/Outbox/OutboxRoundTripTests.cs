using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TreasuryKit.BuildingBlocks.Infrastructure.Outbox;
using TreasuryKit.Core.Infrastructure.Persistence;
using TreasuryKit.IntegrationTests.Infrastructure;
using TreasuryKit.Recon.Infrastructure.Persistence;

namespace TreasuryKit.IntegrationTests.Outbox;

/// <summary>Phase 0 acceptance: publish → handler invoked exactly once, even on redelivery.</summary>
[Collection(ApiCollection.Name)]
public sealed class OutboxRoundTripTests(ApiFactory factory)
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private HandlerCallLog Log => factory.Services.GetRequiredService<HandlerCallLog>();

    private OutboxDispatcher Dispatcher => factory.Services.GetRequiredService<OutboxDispatcher>();

    [Fact]
    public async Task Handler_runs_exactly_once_when_message_is_redelivered()
    {
        var tenantId = Guid.CreateVersion7();
        var evt = new TestEvent(Guid.CreateVersion7(), DateTimeOffset.UtcNow, tenantId, FailFirstDelivery: false);
        await PublishFromCoreAsync(evt);

        await Dispatcher.DispatchPendingAsync(_ct);
        Assert.Equal(1, Log.Count(nameof(CountingHandler), evt.Id));
        Assert.Equal(tenantId, Log.TenantSeen[evt.Id]);
        Assert.NotNull((await GetOutboxMessageAsync(evt.Id)).ProcessedAt);

        // Simulate the outbox delivering the same message again (e.g. crash after delivery, before marking processed).
        await ExecuteOnCoreAsync(db => db.Database.ExecuteSqlAsync(
            $"UPDATE core.outbox_messages SET processed_at = NULL WHERE id = {evt.Id}", _ct));
        await Dispatcher.DispatchPendingAsync(_ct);

        Assert.Equal(1, Log.Count(nameof(CountingHandler), evt.Id));
        Assert.NotNull((await GetOutboxMessageAsync(evt.Id)).ProcessedAt);
        Assert.Equal(2, await CountInboxRowsAsync(evt.Id)); // one per subscriber
    }

    [Fact]
    public async Task Failed_subscriber_is_retried_without_rerunning_successful_ones()
    {
        var evt = new TestEvent(Guid.CreateVersion7(), DateTimeOffset.UtcNow, Guid.CreateVersion7(), FailFirstDelivery: true);
        await PublishFromCoreAsync(evt);

        await Dispatcher.DispatchPendingAsync(_ct);
        var afterFailure = await GetOutboxMessageAsync(evt.Id);
        Assert.Null(afterFailure.ProcessedAt);
        Assert.Equal(1, afterFailure.Attempts);
        Assert.Contains("Simulated transient failure", afterFailure.LastError);

        await Dispatcher.DispatchPendingAsync(_ct);

        Assert.Equal(1, Log.Count(nameof(CountingHandler), evt.Id));
        Assert.Equal(2, Log.Count(nameof(FlakyHandler), evt.Id));
        var delivered = await GetOutboxMessageAsync(evt.Id);
        Assert.NotNull(delivered.ProcessedAt);
        Assert.Equal(2, delivered.Attempts);
        Assert.Null(delivered.LastError);
    }

    private async Task PublishFromCoreAsync(TestEvent evt) =>
        await ExecuteOnCoreAsync(db =>
        {
            db.Publish(evt);
            return db.SaveChangesAsync(_ct);
        });

    private async Task<OutboxMessage> GetOutboxMessageAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<CoreDbContext>()
            .OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == id, _ct);
    }

    private async Task<int> CountInboxRowsAsync(Guid messageId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ReconDbContext>()
            .InboxMessages.CountAsync(m => m.MessageId == messageId, _ct);
    }

    private async Task ExecuteOnCoreAsync(Func<CoreDbContext, Task> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<CoreDbContext>());
    }
}
