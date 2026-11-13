using TreasuryKit.BuildingBlocks.Application.Abstractions;
using TreasuryKit.BuildingBlocks.Application.Messaging;
using TreasuryKit.BuildingBlocks.Domain;
using TreasuryKit.Recon.Domain.Rules;

namespace TreasuryKit.Recon.Application.Configuration;

public sealed record RuleDto(string Key, int Order, bool Enabled, bool Implemented, IReadOnlyDictionary<string, decimal> Parameters);

public sealed record RulesDto(IReadOnlyList<RuleDto> Rules, decimal AutoConfirmThreshold, decimal ProposeThreshold);

public sealed record GetRulesQuery : IQuery<RulesDto>;

public sealed record UpdateRuleCommand(string Key, bool Enabled, int Order, IReadOnlyDictionary<string, decimal>? Parameters) : ICommand;

public sealed record UpdateThresholdsCommand(decimal AutoConfirm, decimal Propose) : ICommand;

internal sealed class RuleHandlers(IReconDbContext db, ReconDefaults defaults, ICurrentTenant tenant, IEnumerable<IMatchingRule> rules) :
    IQueryHandler<GetRulesQuery, RulesDto>,
    ICommandHandler<UpdateRuleCommand>,
    ICommandHandler<UpdateThresholdsCommand>
{
    private readonly HashSet<string> _implemented = rules.Select(r => r.Key).ToHashSet(StringComparer.Ordinal);

    public async Task<Result<RulesDto>> HandleAsync(GetRulesQuery query, CancellationToken cancellationToken)
    {
        var (configs, settings) = await defaults.EnsureAsync(TenantId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken); // persist defaults for tenants created before Phase 3
        return new RulesDto(
            configs.Select(c => new RuleDto(c.RuleKey, c.Order, c.Enabled, _implemented.Contains(c.RuleKey), c.Parameters)).ToList(),
            settings.AutoConfirmThreshold,
            settings.ProposeThreshold);
    }

    public async Task<Result> HandleAsync(UpdateRuleCommand command, CancellationToken cancellationToken)
    {
        var (configs, _) = await defaults.EnsureAsync(TenantId, cancellationToken);
        var config = configs.FirstOrDefault(c => string.Equals(c.RuleKey, command.Key, StringComparison.OrdinalIgnoreCase));
        if (config is null)
        {
            return Error.NotFound("Rule.NotFound", $"Unknown rule '{command.Key}'.");
        }

        var updated = config.Update(command.Enabled, command.Order, command.Parameters);
        if (updated.IsFailure)
        {
            return updated;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> HandleAsync(UpdateThresholdsCommand command, CancellationToken cancellationToken)
    {
        var (_, settings) = await defaults.EnsureAsync(TenantId, cancellationToken);
        var updated = settings.Update(command.AutoConfirm, command.Propose);
        if (updated.IsFailure)
        {
            return updated;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private Guid TenantId => tenant.TenantId ?? throw new InvalidOperationException("No current tenant.");
}
