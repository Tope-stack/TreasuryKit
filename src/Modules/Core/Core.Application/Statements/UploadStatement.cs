using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using TreasuryKit.BuildingBlocks.Application.Abstractions;
using TreasuryKit.BuildingBlocks.Application.Messaging;
using TreasuryKit.BuildingBlocks.Domain;
using TreasuryKit.Core.Application.Common;
using TreasuryKit.Core.Domain.Accounts;
using TreasuryKit.Core.Domain.MappingProfiles;
using TreasuryKit.Core.Domain.Statements;

namespace TreasuryKit.Core.Application.Statements;

/// <param name="Content">The file; the handler buffers it (size is limited by the endpoint).</param>
public sealed record UploadStatementCommand(Guid AccountId, string FileName, Stream Content) : ICommand<UploadStatementResult>;

/// <param name="AlreadyImported">True when this exact file was uploaded before; <paramref name="ImportId"/> is the earlier import.</param>
public sealed record UploadStatementResult(Guid ImportId, bool AlreadyImported);

/// <summary>Job that parses and stores an uploaded statement in the Worker.</summary>
public sealed record ProcessStatementImportJob(Guid? TenantId, Guid ImportId) : BuildingBlocks.Application.Jobs.IJob;

internal sealed class UploadStatementHandler(
    ICoreDbContext db,
    IFileStore files,
    ICurrentTenant tenant,
    ICurrentUser user,
    IClock clock) : ICommandHandler<UploadStatementCommand, UploadStatementResult>
{
    public async Task<Result<UploadStatementResult>> HandleAsync(UploadStatementCommand command, CancellationToken cancellationToken)
    {
        var account = await db.BankAccounts.FirstOrDefaultAsync(a => a.Id == command.AccountId, cancellationToken);
        if (account is null)
        {
            return BankAccountErrors.NotFound;
        }

        if (!account.IsActive)
        {
            return BankAccountErrors.Inactive;
        }

        var format = StatementFormats.FromFileName(command.FileName);
        if (format.IsFailure)
        {
            return format.Error;
        }

        if (account.MappingProfileId is null)
        {
            return Error.Validation("Statement.NoMappingProfile", "Assign a mapping profile to this account before importing CSV/XLSX statements.");
        }

        var profile = await db.MappingProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == account.MappingProfileId, cancellationToken);
        if (profile is null)
        {
            return MappingProfileErrors.NotFound;
        }

        if (!profile.FileType.Matches(format.Value))
        {
            return Error.Validation(
                "Statement.FormatMismatch",
                $"The account's mapping profile '{profile.Name}' reads {profile.FileType} files, but '{command.FileName}' is {format.Value}.");
        }

        using var buffer = new MemoryStream();
        await command.Content.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length == 0)
        {
            return Error.Validation("Statement.Empty", "The uploaded file is empty.");
        }

        buffer.Position = 0;
        var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(buffer, cancellationToken));

        // File-level idempotency: the same bytes for the same account are imported once.
        if (await FindExistingAsync(account.Id, hash, cancellationToken) is { } existing)
        {
            return new UploadStatementResult(existing, AlreadyImported: true);
        }

        var tenantId = tenant.Required();
        var fileKey = $"{tenantId:N}/statements/{hash}{Path.GetExtension(command.FileName).ToLowerInvariant()}";
        buffer.Position = 0;
        await files.SaveAsync(fileKey, buffer, cancellationToken);

        var import = StatementImport.Queue(
            tenantId,
            account.Id,
            account.Currency,
            Path.GetFileName(command.FileName),
            hash,
            fileKey,
            format.Value,
            user.Required(),
            clock.UtcNow);
        db.StatementImports.Add(import);
        db.Enqueue(new ProcessStatementImportJob(tenantId, import.Id));

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // A concurrent upload of the same file may have won the unique index.
            if (await FindExistingAsync(account.Id, hash, cancellationToken) is { } winner)
            {
                return new UploadStatementResult(winner, AlreadyImported: true);
            }

            throw;
        }

        return new UploadStatementResult(import.Id, AlreadyImported: false);
    }

    private async Task<Guid?> FindExistingAsync(Guid accountId, string hash, CancellationToken ct) =>
        await db.StatementImports.AsNoTracking()
            .Where(i => i.AccountId == accountId && i.FileHash == hash)
            .Select(i => (Guid?)i.Id)
            .FirstOrDefaultAsync(ct);
}
