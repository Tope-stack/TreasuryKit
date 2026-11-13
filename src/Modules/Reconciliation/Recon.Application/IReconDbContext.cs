using Microsoft.EntityFrameworkCore;
using TreasuryKit.BuildingBlocks.Application.Abstractions;
using TreasuryKit.Recon.Domain.Configuration;
using TreasuryKit.Recon.Domain.Exceptions;
using TreasuryKit.Recon.Domain.Matches;
using TreasuryKit.Recon.Domain.Runs;

namespace TreasuryKit.Recon.Application;

public interface IReconDbContext : IModuleUnitOfWork
{
    DbSet<ReconciliationRun> Runs { get; }

    DbSet<Match> Matches { get; }

    DbSet<MatchItem> MatchItems { get; }

    DbSet<ReconciledItem> ReconciledItems { get; }

    DbSet<ReconException> Exceptions { get; }

    DbSet<MatchingRuleConfig> RuleConfigs { get; }

    DbSet<ReconSettings> Settings { get; }
}

/// <summary>Anchors this assembly for handler registration and architecture tests.</summary>
public static class AssemblyMarker;
