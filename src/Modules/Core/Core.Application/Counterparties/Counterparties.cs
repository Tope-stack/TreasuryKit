using Microsoft.EntityFrameworkCore;
using TreasuryKit.BuildingBlocks.Application.Abstractions;
using TreasuryKit.BuildingBlocks.Application.Messaging;
using TreasuryKit.BuildingBlocks.Domain;
using TreasuryKit.Core.Application.Common;
using TreasuryKit.Core.Domain.Counterparties;

namespace TreasuryKit.Core.Application.Counterparties;

public sealed record CounterpartyDto(
    Guid Id,
    string Name,
    string NormalizedName,
    IReadOnlyList<string> Aliases,
    string? Country,
    string? DefaultCurrency);

public sealed record CreateCounterpartyCommand(
    string Name,
    IReadOnlyList<string>? Aliases,
    string? Country,
    string? DefaultCurrency) : ICommand<Guid>;

public sealed record ListCounterpartiesQuery(string? Search) : IQuery<IReadOnlyList<CounterpartyDto>>;

internal sealed class CreateCounterpartyHandler(ICoreDbContext db, ICurrentTenant tenant)
    : ICommandHandler<CreateCounterpartyCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateCounterpartyCommand command, CancellationToken cancellationToken)
    {
        var created = Counterparty.Create(tenant.Required(), command.Name, command.Aliases, command.Country, command.DefaultCurrency);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var counterparty = created.Value;
        if (await db.Counterparties.AnyAsync(c => c.NormalizedName == counterparty.NormalizedName, cancellationToken))
        {
            return Error.Conflict("Counterparty.Duplicate", $"A counterparty named '{counterparty.Name}' already exists.");
        }

        db.Counterparties.Add(counterparty);
        await db.SaveChangesAsync(cancellationToken);
        return counterparty.Id;
    }
}

internal sealed class ListCounterpartiesHandler(ICoreDbContext db) : IQueryHandler<ListCounterpartiesQuery, IReadOnlyList<CounterpartyDto>>
{
    public async Task<Result<IReadOnlyList<CounterpartyDto>>> HandleAsync(ListCounterpartiesQuery query, CancellationToken cancellationToken)
    {
        var counterparties = db.Counterparties.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = Domain.Common.TextNormalization.ForName(query.Search);
            counterparties = counterparties.Where(c => c.NormalizedName.Contains(term));
        }

        var list = await counterparties.OrderBy(c => c.Name).Take(500).ToListAsync(cancellationToken);
        return list
            .Select(c => new CounterpartyDto(c.Id, c.Name, c.NormalizedName, c.Aliases, c.Country, c.DefaultCurrency?.Code))
            .ToList();
    }
}
