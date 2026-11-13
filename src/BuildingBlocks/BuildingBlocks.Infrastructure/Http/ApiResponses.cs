namespace TreasuryKit.BuildingBlocks.Infrastructure.Http;

/// <summary>Named response bodies so the OpenAPI document (and the generated UI client) has real types.</summary>
public sealed record CreatedResponse(Guid Id);

public sealed record UploadResponse(Guid ImportId, bool AlreadyImported);

public sealed record ExpectedCreatedResponse(Guid Id, bool AlreadyExisted);

public sealed record MatchCreatedResponse(Guid MatchId);

public sealed record RunAcceptedResponse(Guid RunId);
