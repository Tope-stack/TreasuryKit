using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using TreasuryKit.BuildingBlocks.Application.Abstractions;
using TreasuryKit.BuildingBlocks.Application.Jobs;
using TreasuryKit.BuildingBlocks.Application.Messaging;
using TreasuryKit.BuildingBlocks.Domain;
using TreasuryKit.Core.Application.Common;
using TreasuryKit.Core.Domain.ExpectedTransactions;

namespace TreasuryKit.Core.Application.ExpectedTransactions;

/// <summary>One CSV row as text. Columns: externalId, accountNumber, direction, amount, currency, expectedDate, counterparty, reference, description.</summary>
public sealed record ExpectedTransactionCsvRow(
    int Row,
    string? ExternalId,
    string? AccountNumber,
    string? Direction,
    string? Amount,
    string? Currency,
    string? ExpectedDate,
    string? Counterparty,
    string? Reference,
    string? Description);

public interface IExpectedTransactionCsvReader
{
    Result<IReadOnlyList<ExpectedTransactionCsvRow>> Read(Stream stream);
}

public sealed record UploadExpectedTransactionsCommand(string FileName, Stream Content) : ICommand<UploadExpectedTransactionsResult>;

public sealed record UploadExpectedTransactionsResult(Guid ImportId, bool AlreadyImported);

public sealed record GetExpectedTransactionImportQuery(Guid Id) : IQuery<ExpectedTransactionImportDto>;

public sealed record ExpectedTransactionImportDto(
    Guid Id,
    string FileName,
    ExpectedTransactionImportStatus Status,
    int Created,
    int Updated,
    int Unchanged,
    int Skipped,
    IReadOnlyList<ImportRowError> Errors,
    DateTimeOffset ImportedAt,
    DateTimeOffset? CompletedAt);

public sealed record ProcessExpectedTransactionImportJob(Guid? TenantId, Guid ImportId) : IJob;

internal sealed class UploadExpectedTransactionsHandler(
    ICoreDbContext db, IFileStore files, ICurrentTenant tenant, ICurrentUser user, IClock clock)
    : ICommandHandler<UploadExpectedTransactionsCommand, UploadExpectedTransactionsResult>
{
    public async Task<Result<UploadExpectedTransactionsResult>> HandleAsync(UploadExpectedTransactionsCommand c, CancellationToken cancellationToken)
    {
        if (!string.Equals(Path.GetExtension(c.FileName), ".csv", StringComparison.OrdinalIgnoreCase))
        {
            return Error.Validation("ExpectedTransactionImport.UnsupportedFormat", "Upload expected transactions as a .csv file.");
        }

        using var buffer = new MemoryStream();
        await c.Content.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length == 0)
        {
            return Error.Validation("ExpectedTransactionImport.Empty", "The uploaded file is empty.");
        }

        buffer.Position = 0;
        var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(buffer, cancellationToken));
        if (await FindAsync(hash, cancellationToken) is { } existing)
        {
            return new UploadExpectedTransactionsResult(existing, AlreadyImported: true);
        }

        var tenantId = tenant.Required();
        var key = $"{tenantId:N}/expected/{hash}.csv";
        buffer.Position = 0;
        await files.SaveAsync(key, buffer, cancellationToken);

        var import = ExpectedTransactionImport.Queue(tenantId, Path.GetFileName(c.FileName), hash, key, user.Required(), clock.UtcNow);
        db.ExpectedTransactionImports.Add(import);
        db.Enqueue(new ProcessExpectedTransactionImportJob(tenantId, import.Id));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (await FindAsync(hash, cancellationToken) is { } winner)
            {
                return new UploadExpectedTransactionsResult(winner, AlreadyImported: true);
            }

            throw;
        }

        return new UploadExpectedTransactionsResult(import.Id, AlreadyImported: false);
    }

    private async Task<Guid?> FindAsync(string hash, CancellationToken ct) =>
        await db.ExpectedTransactionImports.AsNoTracking().Where(i => i.FileHash == hash).Select(i => (Guid?)i.Id).FirstOrDefaultAsync(ct);
}

internal sealed class GetExpectedTransactionImportHandler(ICoreDbContext db)
    : IQueryHandler<GetExpectedTransactionImportQuery, ExpectedTransactionImportDto>
{
    public async Task<Result<ExpectedTransactionImportDto>> HandleAsync(GetExpectedTransactionImportQuery q, CancellationToken cancellationToken)
    {
        var i = await db.ExpectedTransactionImports.AsNoTracking().FirstOrDefaultAsync(x => x.Id == q.Id, cancellationToken);
        return i is null
            ? Error.NotFound("ExpectedTransactionImport.NotFound", "Import not found.")
            : new ExpectedTransactionImportDto(i.Id, i.FileName, i.Status, i.Created, i.Updated, i.Unchanged, i.Skipped, i.Errors, i.ImportedAt, i.CompletedAt);
    }
}

/// <summary>
/// Applies a CSV of expected transactions, idempotent by externalId: new ids are created, identical rows are left
/// alone, changed rows update open transactions, rows for settled/cancelled ones are skipped. All in one transaction.
/// </summary>
public sealed class ProcessExpectedTransactionImportHandler(
    ICoreDbContext db, IFileStore files, IExpectedTransactionCsvReader reader, IClock clock)
    : IJobHandler<ProcessExpectedTransactionImportJob>
{
    public async Task HandleAsync(ProcessExpectedTransactionImportJob job, CancellationToken cancellationToken)
    {
        var import = await db.ExpectedTransactionImports.FirstOrDefaultAsync(i => i.Id == job.ImportId, cancellationToken);
        if (import is null || !import.IsPending)
        {
            return;
        }

        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        import.StartProcessing();

        Result<IReadOnlyList<ExpectedTransactionCsvRow>> rows;
        await using (var stream = await files.OpenReadAsync(import.FileKey, cancellationToken))
        {
            rows = reader.Read(stream);
        }

        if (rows.IsFailure)
        {
            import.Fail(rows.Error.Message, clock.UtcNow);
        }
        else
        {
            await ApplyAsync(import, rows.Value, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task OnFailedPermanentlyAsync(ProcessExpectedTransactionImportJob job, string error, CancellationToken cancellationToken)
    {
        var import = await db.ExpectedTransactionImports.FirstOrDefaultAsync(i => i.Id == job.ImportId, cancellationToken);
        if (import is { IsPending: true })
        {
            import.Fail("Processing failed repeatedly. Last error: " + error.Split('\n')[0], clock.UtcNow);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task ApplyAsync(ExpectedTransactionImport import, IReadOnlyList<ExpectedTransactionCsvRow> rows, CancellationToken ct)
    {
        var resolver = new ExpectedTransactionInputResolver(db);
        var now = clock.UtcNow;
        var errors = new List<ImportRowError>();
        int created = 0, updated = 0, unchanged = 0, skipped = 0;

        var externalIds = rows.Select(r => r.ExternalId?.Trim()).Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
        var existing = await db.ExpectedTransactions
            .Where(t => t.Source == ExpectedTransactionSource.Csv && t.ExternalId != null && externalIds.Contains(t.ExternalId))
            .ToDictionaryAsync(t => t.ExternalId!, StringComparer.Ordinal, ct);
        var seenInFile = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            var externalId = row.ExternalId?.Trim();
            if (string.IsNullOrEmpty(externalId))
            {
                errors.Add(new ImportRowError(row.Row, "externalId is required."));
                continue;
            }

            if (!seenInFile.Add(externalId))
            {
                errors.Add(new ImportRowError(row.Row, $"externalId '{externalId}' appears more than once in the file."));
                continue;
            }

            var input = ParseRow(row);
            if (input.IsFailure)
            {
                errors.Add(new ImportRowError(row.Row, input.Error.Message));
                continue;
            }

            var details = await resolver.ResolveAsync(input.Value, ct);
            if (details.IsFailure)
            {
                errors.Add(new ImportRowError(row.Row, details.Error.Message));
                continue;
            }

            if (existing.TryGetValue(externalId, out var current))
            {
                if (current.Matches(details.Value))
                {
                    unchanged++;
                }
                else if (current.Update(details.Value, now) is { IsSuccess: true })
                {
                    db.Publish(ExpectedTransactionEvents.Updated(current, now));
                    updated++;
                }
                else
                {
                    skipped++;
                    errors.Add(new ImportRowError(row.Row, $"'{externalId}' is {current.Status} and can no longer change; row skipped."));
                }

                continue;
            }

            var result = ExpectedTransaction.Create(import.TenantId, details.Value, ExpectedTransactionSource.Csv, externalId, now);
            if (result.IsFailure)
            {
                errors.Add(new ImportRowError(row.Row, result.Error.Message));
                continue;
            }

            db.ExpectedTransactions.Add(result.Value);
            db.Publish(ExpectedTransactionEvents.Created(result.Value, now));
            created++;
        }

        import.Complete(created, updated, unchanged, skipped, errors, now);
    }

    private static Result<ExpectedTransactionInput> ParseRow(ExpectedTransactionCsvRow row)
    {
        if (!decimal.TryParse(row.Amount?.Replace(",", string.Empty, StringComparison.Ordinal), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
        {
            return Error.Validation("Row.Invalid", $"amount '{row.Amount}' is not a number.");
        }

        if (!DateOnly.TryParseExact(row.ExpectedDate?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return Error.Validation("Row.Invalid", $"expectedDate '{row.ExpectedDate}' must be yyyy-MM-dd.");
        }

        return new ExpectedTransactionInput(null, row.AccountNumber, row.Direction, amount, row.Currency, date, row.Counterparty, row.Reference, row.Description);
    }
}
