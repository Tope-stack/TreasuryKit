using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TreasuryKit.BuildingBlocks.Application.Jobs;
using TreasuryKit.BuildingBlocks.Infrastructure.Context;
using TreasuryKit.BuildingBlocks.Infrastructure.Persistence;

namespace TreasuryKit.BuildingBlocks.Infrastructure.Jobs;

public sealed record JobHandlerRegistration(Type JobType, Type InvokerType)
{
    public string TypeName { get; } = IntegrationEventSerializer.TypeName(JobType);
}

public interface IJobInvoker
{
    Task InvokeAsync(object job, CancellationToken cancellationToken);

    Task OnFailedPermanentlyAsync(object job, string error, CancellationToken cancellationToken);
}

internal sealed class JobInvoker<TJob, THandler>(THandler handler) : IJobInvoker
    where TJob : IJob
    where THandler : IJobHandler<TJob>
{
    public Task InvokeAsync(object job, CancellationToken cancellationToken) =>
        handler.HandleAsync((TJob)job, cancellationToken);

    public Task OnFailedPermanentlyAsync(object job, string error, CancellationToken cancellationToken) =>
        handler.OnFailedPermanentlyAsync((TJob)job, error, cancellationToken);
}

/// <summary>Runs one job in its own DI scope with the job's tenant and the system user.</summary>
internal sealed class JobExecutor(IServiceScopeFactory scopes, IEnumerable<JobHandlerRegistration> registrations)
{
    private readonly Dictionary<string, JobHandlerRegistration> _byType =
        registrations.ToDictionary(r => r.TypeName, StringComparer.Ordinal);

    public Task ExecuteAsync(JobRecord record, CancellationToken cancellationToken) =>
        RunAsync(record, (invoker, job) => invoker.InvokeAsync(job, cancellationToken));

    public Task NotifyFailedPermanentlyAsync(JobRecord record, CancellationToken cancellationToken) =>
        RunAsync(record, (invoker, job) => invoker.OnFailedPermanentlyAsync(job, record.LastError ?? "Unknown error", cancellationToken));

    private async Task RunAsync(JobRecord record, Func<IJobInvoker, object, Task> action)
    {
        if (!_byType.TryGetValue(record.Type, out var registration))
        {
            throw new InvalidOperationException($"No handler registered for job type '{record.Type}'.");
        }

        var job = JsonSerializer.Deserialize(record.Payload, registration.JobType, IntegrationEventSerializer.Options)
            ?? throw new InvalidOperationException($"Job {record.Id} has an empty payload.");

        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(record.TenantId);
        scope.ServiceProvider.GetRequiredService<UserContext>().SetSystem();
        var invoker = (IJobInvoker)scope.ServiceProvider.GetRequiredService(registration.InvokerType);
        await action(invoker, job);
    }
}
