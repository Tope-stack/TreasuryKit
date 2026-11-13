using Microsoft.EntityFrameworkCore;
using TreasuryKit.BuildingBlocks.Application.Abstractions;
using TreasuryKit.BuildingBlocks.Application.Jobs;
using TreasuryKit.BuildingBlocks.Application.Messaging;
using TreasuryKit.BuildingBlocks.Domain;
using TreasuryKit.Core.Application.Common;
using TreasuryKit.Core.Application.Statements;
using TreasuryKit.Core.Application.Statements.Parsing;

namespace TreasuryKit.Core.Application.Settings;

public sealed record ReferencePatternsDto(IReadOnlyList<string> InvoicePatterns);

public sealed record GetReferencePatternsQuery : IQuery<ReferencePatternsDto>;

/// <summary>Replaces the tenant's invoice patterns and re-extracts references on existing lines.</summary>
public sealed record SetReferencePatternsCommand(IReadOnlyList<string> InvoicePatterns) : ICommand;

/// <summary>Re-runs normalization and extraction over all of the tenant's lines (e.g. backfill after an upgrade).</summary>
public sealed record StartReextractionCommand : ICommand;

/// <summary>Background re-extraction for one tenant. Idempotent: it recomputes from the stored narration.</summary>
public sealed record ReextractStatementLinesJob(Guid? TenantId) : IJob;

internal sealed class GetReferencePatternsHandler(ICoreDbContext db, ICurrentTenant tenant)
    : IQueryHandler<GetReferencePatternsQuery, ReferencePatternsDto>
{
    public async Task<Result<ReferencePatternsDto>> HandleAsync(GetReferencePatternsQuery query, CancellationToken cancellationToken)
    {
        var tenantId = tenant.Required();
        var current = await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == tenantId, cancellationToken);
        return new ReferencePatternsDto(current.InvoiceReferencePatterns);
    }
}

internal sealed class SetReferencePatternsHandler(ICoreDbContext db, ICurrentTenant tenant) : ICommandHandler<SetReferencePatternsCommand>
{
    public async Task<Result> HandleAsync(SetReferencePatternsCommand command, CancellationToken cancellationToken)
    {
        var tenantId = tenant.Required();
        var current = await db.Tenants.FirstAsync(t => t.Id == tenantId, cancellationToken);
        var result = current.SetInvoiceReferencePatterns(command.InvoicePatterns);
        if (result.IsFailure)
        {
            return result;
        }

        db.Enqueue(new ReextractStatementLinesJob(tenantId));
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class StartReextractionHandler(ICoreDbContext db, ICurrentTenant tenant) : ICommandHandler<StartReextractionCommand>
{
    public async Task<Result> HandleAsync(StartReextractionCommand command, CancellationToken cancellationToken)
    {
        db.Enqueue(new ReextractStatementLinesJob(tenant.Required()));
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed class ReextractStatementLinesHandler(ICoreDbContext db, IStatementLineWriter writer, LineAnalyzer analyzer)
    : IJobHandler<ReextractStatementLinesJob>
{
    private const int BatchSize = 1000;

    public async Task HandleAsync(ReextractStatementLinesJob job, CancellationToken cancellationToken)
    {
        var tenantId = job.TenantId ?? throw new InvalidOperationException("Re-extraction needs a tenant.");
        var tenant = await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == tenantId, cancellationToken);
        var patterns = tenant.GetInvoiceReferencePatterns();

        // Stable ordering by id; updates don't change which lines exist, so offset paging is safe here.
        // The tenant filter applies: the job runs as this tenant.
        for (var offset = 0; ; offset += BatchSize)
        {
            var batch = await db.StatementLines.AsNoTracking()
                .OrderBy(l => l.Id)
                .Skip(offset)
                .Take(BatchSize)
                .Select(l => new { l.Id, l.Narration, l.EndToEndId, l.CounterpartyName })
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                return;
            }

            var analyses = batch
                .Select(l =>
                {
                    var result = analyzer.Analyze(l.Narration, l.EndToEndId, l.CounterpartyName, patterns);
                    return new LineAnalysis(l.Id, result.NormalizedNarration, result.ExtractedRefsJson, result.CounterpartyNameGuess);
                })
                .ToList();
            await writer.UpdateAnalysisAsync(analyses, cancellationToken);
        }
    }
}
