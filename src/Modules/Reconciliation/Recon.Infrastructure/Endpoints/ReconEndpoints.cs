using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TreasuryKit.BuildingBlocks.Application.Messaging;
using TreasuryKit.BuildingBlocks.Infrastructure.Authorization;
using TreasuryKit.BuildingBlocks.Infrastructure.Http;
using TreasuryKit.Recon.Application.Configuration;
using TreasuryKit.Recon.Application.Queries;
using TreasuryKit.Recon.Application.Runs;
using TreasuryKit.Recon.Domain.Exceptions;
using TreasuryKit.Recon.Domain.Matches;

namespace TreasuryKit.Recon.Infrastructure.Endpoints;

/// <summary>Routes under /api/recon (plan §5.7). Manual match operations and reports arrive in Phase 4.</summary>
internal static class ReconEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var runs = app.MapGroup("/runs").WithTags("Runs");
        runs.MapPost("/", async (StartRunCommand command, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.SendAsync(command, ct)).Match(id => Results.Accepted($"/api/recon/runs/{id}", new { runId = id })))
            .RequireAuthorization(Policies.CanPrepare);
        runs.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.QueryAsync(new GetRunQuery(id), ct)).Match(Results.Ok));
        runs.MapGet("/", async (Guid? accountId, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.QueryAsync(new ListRunsQuery(accountId), ct)).Match(Results.Ok));

        var matches = app.MapGroup("/matches").WithTags("Matches");
        matches.MapGet("/", async (MatchStatus? status, Guid? accountId, DateOnly? from, DateOnly? to, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.QueryAsync(new ListMatchesQuery(status, accountId, from, to), ct)).Match(Results.Ok));
        matches.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.QueryAsync(new GetMatchQuery(id), ct)).Match(Results.Ok));

        app.MapGet("/exceptions", async (ExceptionStatus? status, Guid? accountId, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.QueryAsync(new ListExceptionsQuery(status, accountId), ct)).Match(Results.Ok))
            .WithTags("Exceptions");

        var rules = app.MapGroup("/rules").WithTags("Rules");
        rules.MapGet("/", async (IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.QueryAsync(new GetRulesQuery(), ct)).Match(Results.Ok));
        rules.MapPut("/{key}", async (string key, RuleUpdate body, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.SendAsync(new UpdateRuleCommand(key, body.Enabled, body.Order, body.Parameters), ct)).Match(Results.NoContent))
            .RequireAuthorization(Policies.AdminOnly);

        app.MapPut("/settings/thresholds", async (UpdateThresholdsCommand command, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.SendAsync(command, ct)).Match(Results.NoContent))
            .WithTags("Rules")
            .RequireAuthorization(Policies.AdminOnly);
    }

    internal sealed record RuleUpdate(bool Enabled, int Order, Dictionary<string, decimal>? Parameters);
}
