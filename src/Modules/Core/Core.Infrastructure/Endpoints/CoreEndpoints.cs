using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using TreasuryKit.BuildingBlocks.Application.Messaging;
using TreasuryKit.BuildingBlocks.Infrastructure.Authorization;
using TreasuryKit.BuildingBlocks.Infrastructure.Http;
using TreasuryKit.Core.Application.Accounts;
using TreasuryKit.Core.Application.Counterparties;
using TreasuryKit.Core.Application.ExpectedTransactions;
using TreasuryKit.Core.Application.MappingProfiles;
using TreasuryKit.Core.Application.Settings;
using TreasuryKit.Core.Application.Statements;

namespace TreasuryKit.Core.Infrastructure.Endpoints;

public sealed class StatementUploadOptions
{
    public const string SectionName = "Core:StatementUpload";

    public long MaxFileSizeBytes { get; set; } = 20 * 1024 * 1024;
}

/// <summary>Routes under /api/core (plan §4.7). The group already requires <see cref="Policies.CanView"/>.</summary>
internal static class CoreEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        MapAccounts(app);
        MapMappingProfiles(app);
        MapStatements(app);
        MapCounterparties(app);
        MapSettings(app);
        MapExpectedTransactions(app);
    }

    private static void MapExpectedTransactions(IEndpointRouteBuilder app)
    {
        var expected = app.MapGroup("/expected-transactions").WithTags("Expected transactions");

        expected.MapPost("/", async (CreateExpectedTransactionCommand command, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.SendAsync(command, ct)).Match(r => r.AlreadyExisted
                    ? Results.Ok(new ExpectedCreatedResponse(r.Id, true))
                    : Results.Created($"/api/core/expected-transactions/{r.Id}", new ExpectedCreatedResponse(r.Id, false))))
            .Produces<ExpectedCreatedResponse>(StatusCodes.Status201Created)
            .Produces<ExpectedCreatedResponse>()
            .ProducesValidationProblem()
            .RequireAuthorization(Policies.CanPrepare);

        expected.MapPost("/import", async (IFormFile file, IDispatcher dispatcher, IOptions<StatementUploadOptions> options, CancellationToken ct) =>
            {
                if (file.Length > options.Value.MaxFileSizeBytes)
                {
                    return Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: "ExpectedTransactionImport.TooLarge");
                }

                await using var content = file.OpenReadStream();
                return (await dispatcher.SendAsync(new UploadExpectedTransactionsCommand(file.FileName, content), ct)).Match(r => r.AlreadyImported
                    ? Results.Ok(new UploadResponse(r.ImportId, true))
                    : Results.Accepted($"/api/core/expected-transactions/imports/{r.ImportId}", new UploadResponse(r.ImportId, false)));
            })
            .Produces<UploadResponse>(StatusCodes.Status202Accepted)
            .Produces<UploadResponse>()
            .DisableAntiforgery()
            .RequireAuthorization(Policies.CanPrepare);

        expected.MapGet("/imports", async (IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.QueryAsync(new ListExpectedTransactionImportsQuery(), ct)).Match(Results.Ok))
            .Produces<IReadOnlyList<ExpectedTransactionImportDto>>();

        expected.MapGet("/imports/{id:guid}", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.QueryAsync(new GetExpectedTransactionImportQuery(id), ct)).Match(Results.Ok))
            .Produces<ExpectedTransactionImportDto>();

        expected.MapGet("/", async (
                Domain.ExpectedTransactions.ExpectedTransactionStatus? status,
                DateOnly? from,
                DateOnly? to,
                string? currency,
                Guid? accountId,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            (await dispatcher.QueryAsync(new ListExpectedTransactionsQuery(status, from, to, currency, accountId), ct)).Match(Results.Ok))
            .Produces<IReadOnlyList<ExpectedTransactionDto>>();

        expected.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.QueryAsync(new GetExpectedTransactionQuery(id), ct)).Match(Results.Ok))
            .Produces<ExpectedTransactionDto>();

        expected.MapPatch("/{id:guid}", async (Guid id, UpdateExpectedTransactionCommand command, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.SendAsync(command with { Id = id }, ct)).Match(Results.NoContent))
            .RequireAuthorization(Policies.CanPrepare);

        expected.MapPost("/{id:guid}/cancel", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.SendAsync(new CancelExpectedTransactionCommand(id), ct)).Match(Results.NoContent))
            .RequireAuthorization(Policies.CanPrepare);
    }

    private static void MapSettings(IEndpointRouteBuilder app)
    {
        var settings = app.MapGroup("/settings").WithTags("Settings");

        settings.MapGet("/reference-patterns", async (IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.QueryAsync(new GetReferencePatternsQuery(), ct)).Match(Results.Ok))
            .Produces<ReferencePatternsDto>();

        settings.MapPut("/reference-patterns", async (SetReferencePatternsCommand command, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.SendAsync(command, ct)).Match(Results.NoContent))
            .RequireAuthorization(Policies.AdminOnly);

        app.MapPost("/maintenance/reextract", async (IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.SendAsync(new StartReextractionCommand(), ct)).Match(() => Results.Accepted()))
            .WithTags("Maintenance")
            .RequireAuthorization(Policies.AdminOnly);
    }

    private static void MapAccounts(IEndpointRouteBuilder app)
    {
        var accounts = app.MapGroup("/accounts").WithTags("Accounts");

        accounts.MapPost("/", async (CreateBankAccountCommand command, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.SendAsync(command, ct)).Match(id => Results.Created($"/api/core/accounts/{id}", new CreatedResponse(id))))
            .Produces<CreatedResponse>(StatusCodes.Status201Created)
            .RequireAuthorization(Policies.AdminOnly);

        accounts.MapGet("/", async (IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.QueryAsync(new ListBankAccountsQuery(), ct)).Match(Results.Ok))
            .Produces<IReadOnlyList<BankAccountDto>>();

        accounts.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.QueryAsync(new GetBankAccountQuery(id), ct)).Match(Results.Ok))
            .Produces<BankAccountDto>();

        accounts.MapGet("/{id:guid}/imports", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.QueryAsync(new ListStatementImportsQuery(id), ct)).Match(Results.Ok))
            .Produces<IReadOnlyList<StatementImportDto>>();

        accounts.MapGet("/{id:guid}/lines", async (
                Guid id,
                DateOnly? from,
                DateOnly? to,
                int? page,
                int? pageSize,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            (await dispatcher.QueryAsync(new GetStatementLinesQuery(id, from, to, page ?? 1, pageSize ?? 100), ct)).Match(Results.Ok))
            .Produces<Application.Common.PagedResult<StatementLineDto>>();

        accounts.MapPost("/{id:guid}/statements", async (
                Guid id,
                IFormFile file,
                IDispatcher dispatcher,
                IOptions<StatementUploadOptions> options,
                CancellationToken ct) =>
            {
                if (file.Length > options.Value.MaxFileSizeBytes)
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status413PayloadTooLarge,
                        title: "Statement.TooLarge",
                        detail: $"Statement files are limited to {options.Value.MaxFileSizeBytes / (1024 * 1024)} MB.");
                }

                await using var content = file.OpenReadStream();
                var result = await dispatcher.SendAsync(new UploadStatementCommand(id, file.FileName, content), ct);
                return result.Match(r => r.AlreadyImported
                    ? Results.Ok(new UploadResponse(r.ImportId, true))
                    : Results.Accepted($"/api/core/imports/{r.ImportId}", new UploadResponse(r.ImportId, false)));
            })
            .Produces<UploadResponse>(StatusCodes.Status202Accepted)
            .Produces<UploadResponse>()
            .DisableAntiforgery() // API clients authenticate with bearer tokens, not cookies
            .RequireAuthorization(Policies.CanPrepare);
    }

    private static void MapMappingProfiles(IEndpointRouteBuilder app)
    {
        var profiles = app.MapGroup("/mapping-profiles").WithTags("Mapping profiles");

        profiles.MapPost("/", async (CreateMappingProfileCommand command, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.SendAsync(command, ct)).Match(id => Results.Created($"/api/core/mapping-profiles/{id}", new CreatedResponse(id))))
            .Produces<CreatedResponse>(StatusCodes.Status201Created)
            .RequireAuthorization(Policies.AdminOnly);

        profiles.MapGet("/", async (IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.QueryAsync(new ListMappingProfilesQuery(), ct)).Match(Results.Ok))
            .Produces<IReadOnlyList<MappingProfileDto>>();
    }

    private static void MapStatements(IEndpointRouteBuilder app)
    {
        var imports = app.MapGroup("/imports").WithTags("Statement imports");

        imports.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.QueryAsync(new GetStatementImportQuery(id), ct)).Match(Results.Ok))
            .Produces<StatementImportDto>();

        imports.MapPost("/{id:guid}/acknowledge-mismatch", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.SendAsync(new AcknowledgeBalanceMismatchCommand(id), ct)).Match(Results.NoContent))
            .RequireAuthorization(Policies.AdminOnly);
    }

    private static void MapCounterparties(IEndpointRouteBuilder app)
    {
        var counterparties = app.MapGroup("/counterparties").WithTags("Counterparties");

        counterparties.MapPost("/", async (CreateCounterpartyCommand command, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.SendAsync(command, ct)).Match(id => Results.Created($"/api/core/counterparties/{id}", new CreatedResponse(id))))
            .Produces<CreatedResponse>(StatusCodes.Status201Created)
            .RequireAuthorization(Policies.AdminOnly);

        counterparties.MapGet("/", async (string? search, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.QueryAsync(new ListCounterpartiesQuery(search), ct)).Match(Results.Ok))
            .Produces<IReadOnlyList<CounterpartyDto>>();
    }
}
