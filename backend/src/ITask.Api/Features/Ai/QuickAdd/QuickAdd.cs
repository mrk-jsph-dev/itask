using System.Globalization;
using System.Text;
using FluentValidation;
using ITask.Api.Common.Ai;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Cards;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using ITask.Api.Common.Validation;
using ITask.Api.Domain;
using ITask.Api.Infrastructure.Ai;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Ai.QuickAdd;

/// <summary>
/// Turns one sentence into a card draft (title, due date, priority, existing labels, list). Editor or above.
/// Nothing is saved; the client creates the card after the user confirms.
/// </summary>
public static class QuickAdd
{
    public const int TextMaxLength = 300;

    public sealed record Request(string Text);

    /// <param name="DueDate">A calendar day in the caller's time zone; the client picks the time.</param>
    public sealed record Response(
        string Title, DateOnly? DueDate, CardPriority Priority, IReadOnlyList<Guid> LabelIds, Guid? ListId, int Remaining);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator() => RuleFor(r => r.Text).NotEmpty().MaximumLength(TextMaxLength);
    }

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapPost("/boards/{boardId:guid}/ai/quick-add", Handle)
                .RequireAi()
                .WithValidation<Request>();

        private static async Task<Results<Ok<Response>, ValidationProblem, ProblemHttpResult>> Handle(
            Guid boardId,
            Request request,
            AppDbContext db,
            IBoardAccess access,
            ICurrentUser user,
            AiGateway gateway,
            TimeProvider time,
            CancellationToken ct)
        {
            if (await CardGuard.CheckEditorAsync(boardId, db, access, ct) is { } denied)
            {
                return denied;
            }

            var labels = await db.Labels.AsNoTracking().Where(l => l.BoardId == boardId)
                .Select(l => new { l.Id, l.Name }).ToListAsync(ct);
            var lists = await db.Lists.AsNoTracking().Where(l => l.BoardId == boardId && l.ArchivedAt == null)
                .OrderBy(l => l.Position).Select(l => new { l.Id, l.Name }).ToListAsync(ct);
            var zoneId = await db.Users.Where(u => u.Id == user.Id).Select(u => u.TimeZone).FirstAsync(ct);
            var today = Recurrences.LocalDate(time.GetUtcNow(), Recurrences.Zone(zoneId));

            var prompt = new StringBuilder()
                .Append(CultureInfo.InvariantCulture, $"Today: {today:yyyy-MM-dd} ({today.DayOfWeek})\n")
                .Append("Board labels: ").AppendJoin(", ", labels.Select(l => l.Name)).Append('\n')
                .Append("Board lists: ").AppendJoin(", ", lists.Select(l => l.Name)).Append("\n\n")
                .Append("Text:\n").Append(request.Text.Trim())
                .ToString();

            var labelNames = labels.Select(l => l.Name).ToList();
            var listNames = lists.Select(l => l.Name).ToList();
            var outcome = await gateway.RunAsync(
                AiPrompts.QuickAdd, prompt, 300, json => AiDrafts.ParseQuickAdd(json, labelNames, listNames), ct, AiDrafts.QuickAddSchema);
            if (outcome.Problem is { } problem)
            {
                return problem;
            }

            var draft = outcome.Value!;
            return TypedResults.Ok(new Response(
                draft.Title,
                draft.DueDate,
                draft.Priority,
                [.. draft.Labels.Select(name => labels.First(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase)).Id)],
                draft.List is { } list ? lists.First(l => string.Equals(l.Name, list, StringComparison.OrdinalIgnoreCase)).Id : null,
                outcome.Remaining));
        }
    }
}
