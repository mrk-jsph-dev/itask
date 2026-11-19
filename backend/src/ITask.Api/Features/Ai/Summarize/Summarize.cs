using System.Text;
using ITask.Api.Common.Ai;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Cards;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using ITask.Api.Infrastructure.Ai;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Ai.Summarize;

/// <summary>
/// One-paragraph summary of a card from its fields, description, latest comments and activity.
/// Editor or above; nothing is saved (the client may post it as a comment).
/// </summary>
public static class Summarize
{
    public const int MaxSummaryLength = 1_500;

    private const int RecentItems = 50;

    public sealed record Response(string Summary, int Remaining);

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapPost("/boards/{boardId:guid}/cards/{cardId:guid}/ai/summary", Handle)
                .RequireAi();

        private static async Task<Results<Ok<Response>, ProblemHttpResult>> Handle(
            Guid boardId, Guid cardId, AppDbContext db, IBoardAccess access, ICurrentUser user, AiGateway gateway, CancellationToken ct)
        {
            if (await CardGuard.CheckEditorAsync(boardId, db, access, ct) is { } denied)
            {
                return denied;
            }

            var card = await db.Cards.AsNoTracking()
                .Where(c => c.Id == cardId && c.BoardId == boardId)
                .Select(c => new
                {
                    c.Title,
                    c.Description,
                    List = c.List!.Name,
                    c.Priority,
                    c.DueAt,
                    c.CompletedAt,
                    Assignees = c.Assignees.Select(a => a.User!.DisplayName).ToList(),
                    ChecklistTotal = c.Checklist.Count,
                    ChecklistDone = c.Checklist.Count(i => i.IsDone),
                })
                .FirstOrDefaultAsync(ct);
            if (card is null)
            {
                return CardGuard.NotFound();
            }

            var zone = Recurrences.Zone(await db.Users.Where(u => u.Id == user.Id).Select(u => u.TimeZone).FirstAsync(ct));
            var comments = (await db.Comments.AsNoTracking()
                    .Where(c => c.CardId == cardId)
                    .Select(c => new { Author = c.Author!.DisplayName, c.Body, c.CreatedAt })
                    .ToListAsync(ct))
                .OrderByDescending(c => c.CreatedAt).Take(RecentItems).Reverse();
            var activity = await db.Activity.AsNoTracking()
                .Where(a => a.BoardId == boardId && a.CardId == cardId)
                .OrderByDescending(a => a.CreatedAt)
                .Take(RecentItems)
                .Select(a => new { Actor = a.Actor!.DisplayName, a.Type, a.Data, a.CreatedAt })
                .ToListAsync(ct);

            var prompt = new StringBuilder()
                .Append("Title: ").Append(card.Title).Append('\n')
                .Append("List: ").Append(card.List).Append('\n')
                .Append("Priority: ").Append(card.Priority).Append('\n')
                .Append("Due: ").Append(card.DueAt is { } due ? AiText.Day(due, zone) : "none").Append('\n')
                .Append("Completed: ").Append(card.CompletedAt is { } done ? AiText.Day(done, zone) : "no").Append('\n')
                .Append("Assignees: ").Append(card.Assignees.Count > 0 ? string.Join(", ", card.Assignees) : "nobody").Append('\n')
                .Append("Checklist: ").Append(card.ChecklistDone).Append(" of ").Append(card.ChecklistTotal).Append(" done\n\n")
                .Append("Description:\n").Append(AiText.Cut(card.Description, 4_000)).Append("\n\n")
                .Append("Comments (oldest first):\n");
            foreach (var comment in comments)
            {
                prompt.Append("- ").Append(comment.Author).Append(", ").Append(AiText.Day(comment.CreatedAt, zone)).Append(": ")
                    .Append(AiText.Cut(comment.Body, 600)).Append('\n');
            }

            prompt.Append("\nActivity (oldest first):\n");
            foreach (var entry in Enumerable.Reverse(activity))
            {
                prompt.Append("- ").Append(entry.Actor).Append(", ").Append(AiText.Day(entry.CreatedAt, zone)).Append(": ")
                    .Append(entry.Type).Append(' ').Append(AiText.Cut(entry.Data, 200)).Append('\n');
            }

            var outcome = await gateway.RunAsync(
                AiPrompts.Summary, AiText.Cut(prompt.ToString(), AiText.MaxPromptChars), 300, text => AiDrafts.ParseText(text, MaxSummaryLength), ct);
            return outcome.Problem is { } problem
                ? problem
                : TypedResults.Ok(new Response(outcome.Value!, outcome.Remaining));
        }
    }
}
