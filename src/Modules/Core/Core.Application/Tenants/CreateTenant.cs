using TreasuryKit.BuildingBlocks.Application.Abstractions;
using TreasuryKit.BuildingBlocks.Application.Messaging;
using TreasuryKit.BuildingBlocks.Domain;
using TreasuryKit.Core.Contracts.Tenants;
using TreasuryKit.Core.Domain.Tenants;

namespace TreasuryKit.Core.Application.Tenants;

public sealed record CreateTenantCommand(string Name, string? BusinessTimeZone) : ICommand<Guid>;

internal sealed class CreateTenantHandler(ICoreDbContext db, IClock clock) : ICommandHandler<CreateTenantCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateTenantCommand command, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var created = Tenant.Create(command.Name, command.BusinessTimeZone, now);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var tenant = created.Value;
        db.Tenants.Add(tenant);
        db.Publish(new TenantCreated(Guid.CreateVersion7(), now, tenant.Id, tenant.Name, tenant.BusinessTimeZone));
        await db.SaveChangesAsync(cancellationToken);

        return tenant.Id;
    }
}
