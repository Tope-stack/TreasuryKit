using Microsoft.EntityFrameworkCore;
using TreasuryKit.BuildingBlocks.Application.Abstractions;
using TreasuryKit.Core.Domain.Accounts;
using TreasuryKit.Core.Domain.Counterparties;
using TreasuryKit.Core.Domain.ExpectedTransactions;
using TreasuryKit.Core.Domain.MappingProfiles;
using TreasuryKit.Core.Domain.Statements;
using TreasuryKit.Core.Domain.Tenants;

namespace TreasuryKit.Core.Application;

/// <summary>The Core module's persistence, as seen by its handlers.</summary>
public interface ICoreDbContext : IModuleUnitOfWork
{
    DbSet<Tenant> Tenants { get; }

    DbSet<BankAccount> BankAccounts { get; }

    DbSet<BankMappingProfile> MappingProfiles { get; }

    DbSet<Counterparty> Counterparties { get; }

    DbSet<StatementImport> StatementImports { get; }

    DbSet<StatementLine> StatementLines { get; }

    DbSet<ExpectedTransaction> ExpectedTransactions { get; }

    DbSet<ExpectedTransactionImport> ExpectedTransactionImports { get; }
}
