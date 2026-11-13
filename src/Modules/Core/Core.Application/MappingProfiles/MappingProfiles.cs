using Microsoft.EntityFrameworkCore;
using TreasuryKit.BuildingBlocks.Application.Abstractions;
using TreasuryKit.BuildingBlocks.Application.Messaging;
using TreasuryKit.BuildingBlocks.Domain;
using TreasuryKit.Core.Application.Common;
using TreasuryKit.Core.Domain.MappingProfiles;

namespace TreasuryKit.Core.Application.MappingProfiles;

public sealed record MappingProfileDto(
    Guid Id,
    bool IsSystem,
    string BankCode,
    string Name,
    TabularFileType FileType,
    int HeaderRowIndex,
    int SkipRows,
    string? Delimiter,
    IReadOnlyList<string> DateFormats,
    string Culture,
    ColumnMap ColumnMap,
    AmountMode AmountMode,
    BalanceLocator? OpeningBalanceLocator,
    BalanceLocator? ClosingBalanceLocator);

public sealed record CreateMappingProfileCommand(
    string BankCode,
    string Name,
    TabularFileType FileType,
    int HeaderRowIndex,
    int SkipRows,
    string? Delimiter,
    IReadOnlyList<string> DateFormats,
    string? Culture,
    ColumnMap ColumnMap,
    AmountMode AmountMode,
    BalanceLocator? OpeningBalanceLocator,
    BalanceLocator? ClosingBalanceLocator) : ICommand<Guid>;

public sealed record ListMappingProfilesQuery : IQuery<IReadOnlyList<MappingProfileDto>>;

internal sealed class CreateMappingProfileHandler(ICoreDbContext db, ICurrentTenant tenant)
    : ICommandHandler<CreateMappingProfileCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateMappingProfileCommand c, CancellationToken cancellationToken)
    {
        var created = BankMappingProfile.Create(
            tenant.Required(),
            c.BankCode,
            c.Name,
            c.FileType,
            c.HeaderRowIndex,
            c.SkipRows,
            c.Delimiter,
            c.DateFormats,
            c.Culture,
            c.ColumnMap,
            c.AmountMode,
            c.OpeningBalanceLocator,
            c.ClosingBalanceLocator);
        if (created.IsFailure)
        {
            return created.Error;
        }

        db.MappingProfiles.Add(created.Value);
        await db.SaveChangesAsync(cancellationToken);
        return created.Value.Id;
    }
}

internal sealed class ListMappingProfilesHandler(ICoreDbContext db)
    : IQueryHandler<ListMappingProfilesQuery, IReadOnlyList<MappingProfileDto>>
{
    public async Task<Result<IReadOnlyList<MappingProfileDto>>> HandleAsync(ListMappingProfilesQuery query, CancellationToken cancellationToken)
    {
        // The query filter returns system-wide profiles plus the current tenant's own.
        var profiles = await db.MappingProfiles.AsNoTracking()
            .OrderBy(p => p.TenantId != null)
            .ThenBy(p => p.Name)
            .ToListAsync(cancellationToken);

        return profiles.Select(p => new MappingProfileDto(
                p.Id,
                p.IsSystem,
                p.BankCode,
                p.Name,
                p.FileType,
                p.HeaderRowIndex,
                p.SkipRows,
                p.Delimiter,
                p.DateFormats,
                p.Culture,
                p.ColumnMap,
                p.AmountMode,
                p.OpeningBalanceLocator,
                p.ClosingBalanceLocator))
            .ToList();
    }
}
