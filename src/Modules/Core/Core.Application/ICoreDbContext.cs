using Microsoft.EntityFrameworkCore;
using TreasuryKit.BuildingBlocks.Application.Abstractions;
using TreasuryKit.Core.Domain.Tenants;

namespace TreasuryKit.Core.Application;

/// <summary>The Core module's persistence, as seen by its handlers.</summary>
public interface ICoreDbContext : IModuleUnitOfWork
{
    DbSet<Tenant> Tenants { get; }
}
