using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TreasuryKit.BuildingBlocks.Application.Messaging;
using TreasuryKit.BuildingBlocks.Infrastructure.Authorization;
using TreasuryKit.BuildingBlocks.Infrastructure.Http;
using TreasuryKit.Recon.Application.Configuration;
using TreasuryKit.Recon.Application.Matches;
using TreasuryKit.Recon.Application.Queries;
using TreasuryKit.Recon.Application.Reports;
using TreasuryKit.Recon.Application.Runs;
using TreasuryKit.Recon.Domain.Exceptions;
using TreasuryKit.Recon.Domain.Matches;

namespace TreasuryKit.Recon.Infrastructure.Endpoints;

/// <summary>Routes under /api/recon (plan §5.7).</summary>
internal static class ReconEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var runs = app.MapGroup("/runs").WithTags("Runs");
        runs.MapPost("/", async (StartRunCommand command, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.SendAsync(command, ct)).Match(id => Results.Accepted($"/api/recon/runs/{id}", new RunAcceptedResponse(id))))
            .Produces<RunAcceptedResponse>(StatusCodes.Status202Accepted)
            .RequireAuthorization(Policies.CanPrepare);
        runs.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.QueryAsync(new GetRunQuery(id), ct)).Match(Results.Ok))
            .Produces<RunDto>();
        runs.MapGet("/", async (Guid? accountId, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.QueryAsync(new ListRunsQuery(accountId), ct)).Match(Results.Ok))
            .Produces<IReadOnlyList<RunDto>>();

        var matches = app.MapGroup("/matches").WithTags("Matches");
        matches.MapGet("/", async (MatchStatus? status, Guid? accountId, DateOnly? from, DateOnly? to, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.QueryAsync(new ListMatchesQuery(status, accountId, from, to), ct)).Match(Results.Ok))
            .Produces<IReadOnlyList<MatchDto>>();
        matches.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.QueryAsync(new GetMatchQuery(id), ct)).Match(Results.Ok))
            .Produces<MatchDto>();

        // Manual operations with maker-checker (plan §5.5).
        matches.MapPost("/", async (ProposeManualMatchCommand command, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.SendAsync(command, ct)).Match(id => Results.Created($"/api/recon/matches/{id}", new MatchCreatedResponse(id))))
            .Produces<MatchCreatedResponse>(StatusCodes.Status201Created)
            .RequireAuthorization(Policies.CanPrepare);
        matches.MapPost("/{id:guid}/confirm", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.SendAsync(new ConfirmMatchCommand(id), ct)).Match(Results.NoContent))
            .RequireAuthorization(Policies.CanApprove);
        matches.MapPost("/{id:guid}/reject", async (Guid id, ReasonBody? body, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.SendAsync(new RejectMatchCommand(id, body?.Reason), ct)).Match(Results.NoContent))
            .RequireAuthorization(Policies.CanApprove);
        matches.MapPost("/{id:guid}/reverse", async (Guid id, ReasonBody body, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.SendAsync(new ReverseMatchCommand(id, body.Reason), ct)).Match(Results.NoContent))
            .RequireAuthorization(Policies.CanApprove);
        matches.MapPost("/confirm-batch", async (ConfirmMatchesCommand command, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.SendAsync(command, ct)).Match(Results.Ok))
            .Produces<BatchConfirmResult>()
            .RequireAuthorization(Policies.CanApprove);

        var exceptions = app.MapGroup("/exceptions").WithTags("Exceptions");
        exceptions.MapGet("/", async (ExceptionStatus? status, Guid? accountId, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.QueryAsync(new ListExceptionsQuery(status, accountId), ct)).Match(Results.Ok))
            .Produces<IReadOnlyList<ExceptionDto>>();
        exceptions.MapPost("/{id:guid}/resolve", async (Guid id, NoteBody body, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.SendAsync(new ResolveExceptionCommand(id, body.Note), ct)).Match(Results.NoContent))
            .RequireAuthorization(Policies.CanPrepare);
        exceptions.MapPost("/{id:guid}/ignore", async (Guid id, NoteBody body, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.SendAsync(new IgnoreExceptionCommand(id, body.Note), ct)).Match(Results.NoContent))
            .RequireAuthorization(Policies.CanPrepare);

        // Queues for the UI: matches/exceptions with the details of every item involved.
        app.MapGet("/review-queue", async (Guid accountId, MatchStatus? status, DateOnly? from, DateOnly? to, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.QueryAsync(new MatchQueueQuery(accountId, status ?? MatchStatus.Proposed, from, to), ct)).Match(Results.Ok))
            .WithTags("Matches")
            .Produces<IReadOnlyList<MatchDetails>>();
        app.MapGet("/exception-queue", async (Guid accountId, ExceptionStatus? status, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.QueryAsync(new ExceptionQueueQuery(accountId, status ?? ExceptionStatus.Open), ct)).Match(Results.Ok))
            .WithTags("Exceptions")
            .Produces<IReadOnlyList<ExceptionDetails>>();

        var reports = app.MapGroup("/reports").WithTags("Reports");
        reports.MapGet("/summary", async (Guid accountId, DateOnly from, DateOnly to, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.QueryAsync(new GetSummaryQuery(accountId, from, to), ct)).Match(Results.Ok))
            .Produces<ReconciliationSummary>();
        reports.MapGet("/export", async (Guid accountId, DateOnly from, DateOnly to, string? format, IDispatcher dispatcher, CancellationToken ct) =>
        {
            if (!Enum.TryParse<ExportFormat>(format ?? nameof(ExportFormat.Csv), ignoreCase: true, out var exportFormat) || !Enum.IsDefined(exportFormat))
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Report.InvalidFormat", detail: "format must be csv or xlsx.");
            }

            return (await dispatcher.QueryAsync(new ExportQuery(accountId, from, to, exportFormat), ct))
                .Match(file => Results.File(file.Content, file.ContentType, file.FileName));
        });

        var rules = app.MapGroup("/rules").WithTags("Rules");
        rules.MapGet("/", async (IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.QueryAsync(new GetRulesQuery(), ct)).Match(Results.Ok))
            .Produces<RulesDto>();
        rules.MapPut("/{key}", async (string key, RuleUpdate body, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.SendAsync(new UpdateRuleCommand(key, body.Enabled, body.Order, body.Parameters), ct)).Match(Results.NoContent))
            .RequireAuthorization(Policies.AdminOnly);

        app.MapPut("/settings/thresholds", async (UpdateThresholdsCommand command, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.SendAsync(command, ct)).Match(Results.NoContent))
            .WithTags("Rules")
            .RequireAuthorization(Policies.AdminOnly);
    }

    internal sealed record RuleUpdate(bool Enabled, int Order, Dictionary<string, decimal>? Parameters);

    internal sealed record ReasonBody(string? Reason);

    internal sealed record NoteBody(string? Note);
}
