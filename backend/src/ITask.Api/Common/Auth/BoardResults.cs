using Microsoft.AspNetCore.Http.HttpResults;

namespace ITask.Api.Common.Auth;

/// <summary>Problem responses shared by board-scoped slices.</summary>
public static class BoardResults
{
    /// <summary>403 for a member whose role is below the slice's minimum.</summary>
    public static ProblemHttpResult Forbidden() => TypedResults.Problem(
        title: "Your role on this board does not allow this.",
        statusCode: StatusCodes.Status403Forbidden);

    public static ProblemHttpResult Conflict(string title) => TypedResults.Problem(
        title: title,
        statusCode: StatusCodes.Status409Conflict);
}
