using Microsoft.AspNetCore.Http;
using TreasuryKit.BuildingBlocks.Domain;

namespace TreasuryKit.BuildingBlocks.Infrastructure.Http;

public static class ResultHttpExtensions
{
    /// <summary>Maps an expected failure to an RFC 7807 ProblemDetails response.</summary>
    public static IResult ToProblem(this Error error) =>
        Results.Problem(
            statusCode: error.Type switch
            {
                ErrorType.Validation => StatusCodes.Status400BadRequest,
                ErrorType.NotFound => StatusCodes.Status404NotFound,
                ErrorType.Conflict => StatusCodes.Status409Conflict,
                ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
                ErrorType.Forbidden => StatusCodes.Status403Forbidden,
                _ => StatusCodes.Status422UnprocessableEntity,
            },
            title: error.Code,
            detail: error.Message);

    public static IResult Match<T>(this Result<T> result, Func<T, IResult> onSuccess) =>
        result.IsSuccess ? onSuccess(result.Value) : result.Error.ToProblem();

    public static IResult Match(this Result result, Func<IResult> onSuccess) =>
        result.IsSuccess ? onSuccess() : result.Error.ToProblem();
}
