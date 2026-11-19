using Microsoft.AspNetCore.Http.HttpResults;

namespace ITask.Api.Common.Ai;

public static class AiResults
{
    public static ProblemHttpResult Disabled() => TypedResults.Problem(
        title: "AI is not enabled.",
        statusCode: StatusCodes.Status404NotFound);

    public static ProblemHttpResult QuotaSpent(int limit) => TypedResults.Problem(
        title: $"You have used all {limit} AI requests for today. Try again tomorrow.",
        statusCode: StatusCodes.Status429TooManyRequests);

    public static ProblemHttpResult AppBudgetSpent() => TypedResults.Problem(
        title: "AI has reached its limit for today. Try again tomorrow.",
        statusCode: StatusCodes.Status429TooManyRequests);

    public static ProblemHttpResult Unusable() => TypedResults.Problem(
        title: "The AI could not give a usable answer. Try again or rephrase.",
        statusCode: StatusCodes.Status502BadGateway);
}
