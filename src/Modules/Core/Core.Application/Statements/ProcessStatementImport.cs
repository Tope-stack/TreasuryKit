using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TreasuryKit.BuildingBlocks.Application.Abstractions;
using TreasuryKit.BuildingBlocks.Application.Jobs;
using TreasuryKit.BuildingBlocks.Domain;
using TreasuryKit.Core.Application.Statements.Parsing;
using TreasuryKit.Core.Contracts.Statements;
using TreasuryKit.Core.Domain.Common;
using TreasuryKit.Core.Domain.Statements;

namespace TreasuryKit.Core.Application.Statements;

/// <summary>
/// Parse → validate currency → fingerprint → insert (skipping duplicates) → balance check → complete + event.
/// Everything after parsing runs in one transaction, so a retry starts from a clean slate.
/// </summary>
public sealed class ProcessStatementImportHandler(
    ICoreDbContext db,
    IFileStore files,
    IStatementParserResolver parsers,
    IStatementLineWriter lineWriter,
    IClock clock,
    ILogger<ProcessStatementImportHandler> logger) : IJobHandler<ProcessStatementImportJob>
{
    public async Task HandleAsync(ProcessStatementImportJob job, CancellationToken cancellationToken)
    {
        var import = await db.StatementImports.FirstOrDefaultAsync(i => i.Id == job.ImportId, cancellationToken);
        if (import is null || !import.IsPending)
        {
            return; // already processed (job re-run) or gone
        }

        var account = await db.BankAccounts.AsNoTracking().FirstAsync(a => a.Id == import.AccountId, cancellationToken);
        var profile = account.MappingProfileId is { } profileId
            ? await db.MappingProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == profileId, cancellationToken)
            : null;

        import.StartParsing();
        await db.SaveChangesAsync(cancellationToken);

        var parsed = await ParseAsync(import, profile, cancellationToken);

        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        var statement = parsed.IsSuccess ? parsed.Value : null;
        var problem = parsed.IsFailure ? parsed.Error.Message : Validate(statement!, account.Currency);

        if (problem is not null)
        {
            import.Fail(problem, clock.UtcNow);
            logger.LogInformation("Statement import {ImportId} failed: {Problem}", import.Id, problem);
        }
        else
        {
            await StoreAsync(import, statement!, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task OnFailedPermanentlyAsync(ProcessStatementImportJob job, string error, CancellationToken cancellationToken)
    {
        var import = await db.StatementImports.FirstOrDefaultAsync(i => i.Id == job.ImportId, cancellationToken);
        if (import is { IsPending: true })
        {
            import.Fail("Processing failed repeatedly; see logs. Last error: " + error.Split('\n')[0], clock.UtcNow);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<Result<ParsedStatement>> ParseAsync(
        StatementImport import,
        Domain.MappingProfiles.BankMappingProfile? profile,
        CancellationToken cancellationToken)
    {
        var parser = parsers.Resolve(import.Format);
        if (parser.IsFailure)
        {
            return parser.Error;
        }

        await using var stream = await files.OpenReadAsync(import.FileKey, cancellationToken);
        return await parser.Value.ParseAsync(stream, new ParseContext(import.FileName, profile), cancellationToken);
    }

    private static string? Validate(ParsedStatement statement, Currency accountCurrency)
    {
        if (statement.Currency is not null && statement.Currency != accountCurrency)
        {
            return $"Statement currency {statement.Currency.Code} does not match account currency {accountCurrency.Code}.";
        }

        var foreign = statement.Lines.FirstOrDefault(l =>
            !string.IsNullOrWhiteSpace(l.CurrencyCode)
            && !string.Equals(l.CurrencyCode.Trim(), accountCurrency.Code, StringComparison.OrdinalIgnoreCase));
        if (foreign is not null)
        {
            return $"Row {foreign.RowNumber}: currency {foreign.CurrencyCode} does not match account currency {accountCurrency.Code}.";
        }

        return statement.Lines.Count == 0 ? "No transactions found. Check that the account's mapping profile matches this file." : null;
    }

    private async Task StoreAsync(StatementImport import, ParsedStatement statement, CancellationToken cancellationToken)
    {
        var parsedLines = statement.Lines;
        var fingerprints = LineFingerprint.ComputeAll(
            import.AccountId,
            parsedLines,
            l => (l.ValueDate, l.Amount, l.BankReference, l.Narration));

        var lines = parsedLines
            .Select((l, i) => new StatementLine(
                import.TenantId,
                import.AccountId,
                import.Id,
                l.RowNumber,
                l.BookingDate,
                l.ValueDate,
                new Money(l.Amount, import.Currency),
                l.Narration,
                TextNormalization.ForFingerprint(l.Narration),
                l.BankReference,
                fingerprints[i],
                l.RunningBalance))
            .ToList();

        var inserted = await lineWriter.InsertAsync(lines, cancellationToken);

        var balanceCheck = BalanceCheck.Run(
            statement.OpeningBalance,
            statement.ClosingBalance,
            parsedLines.Select(l => (l.RowNumber, l.Amount, l.RunningBalance)).ToList());

        import.Complete(
            statement.PeriodStart ?? parsedLines.Min(l => l.BookingDate),
            statement.PeriodEnd ?? parsedLines.Max(l => l.BookingDate),
            statement.OpeningBalance,
            statement.ClosingBalance,
            lines.Count,
            lines.Count - inserted,
            balanceCheck,
            clock.UtcNow);

        if (import.Status == StatementImportStatus.Completed)
        {
            db.Publish(StatementImportEvents.Imported(import, inserted, clock.UtcNow));
        }

        logger.LogInformation(
            "Statement import {ImportId}: {Status}, {Lines} lines, {Duplicates} duplicates",
            import.Id,
            import.Status,
            import.LineCount,
            import.DuplicateLineCount);
    }
}

internal static class StatementImportEvents
{
    public static StatementImported Imported(StatementImport import, int newLineCount, DateTimeOffset now) =>
        new(Guid.CreateVersion7(), now, import.TenantId, import.AccountId, import.Id, import.PeriodStart, import.PeriodEnd, newLineCount);
}
