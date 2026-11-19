using System.Text.Json;
using System.Text.Json.Nodes;
using ITask.Api.Common.Activity;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Automations;
using ITask.Api.Common.Cards;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Automations.UndoAutomationRun;

/// <summary>
/// Reverts what one automation run changed on its card. The user whose change triggered it, or an
/// Owner. 409 when it was already undone, or when a field it set has been changed since (the newer
/// change wins). Notifications it sent stay.
/// </summary>
public static class UndoAutomationRun
{
    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapPost("/boards/{boardId:guid}/automation-runs/{runId:guid}/undo", Handle)
                .WithTags("Automations")
                .RequireAuthorization();

        private static async Task<Results<Ok<CardSummary>, ProblemHttpResult>> Handle(
            Guid boardId, Guid runId, AppDbContext db, IBoardAccess access, ICurrentUser user, TimeProvider clock, CancellationToken ct)
        {
            if (await CardGuard.CheckEditorAsync(boardId, db, access, ct) is { } denied)
            {
                return denied;
            }

            var run = await db.AutomationRuns.FirstOrDefaultAsync(r => r.Id == runId && r.BoardId == boardId, ct);
            if (run is null)
            {
                return CardGuard.NotFound();
            }

            if (run.TriggeredById != user.Id && !await access.HasAtLeastAsync(boardId, BoardRole.Owner, ct))
            {
                return BoardResults.Forbidden();
            }

            if (run.UndoneAt is not null || run.UndoJson is null)
            {
                return BoardResults.Conflict("This automation run can't be undone any more.");
            }

            var card = await db.Cards.FirstOrDefaultAsync(c => c.Id == run.CardId && c.BoardId == boardId, ct);
            if (card is null)
            {
                return CardGuard.NotFound();
            }

            var changes = JsonSerializer.Deserialize<RunChanges>(run.UndoJson, AutomationSpec.Json)!;
            if ((changes.List is { } list && card.ListId != list.ToId)
                || (changes.CompletedAt is { } completed && card.CompletedAt != completed.After)
                || (changes.DueAt is { } due && card.DueAt != due.After)
                || (changes.ArchivedAt is { } archived && card.ArchivedAt != archived.After)
                || (changes.Priority is { } priority && card.Priority != priority.After))
            {
                return BoardResults.Conflict("The card changed after the automation ran, so it can't be undone.");
            }

            if (changes.List is { } move)
            {
                if (!await db.Lists.AnyAsync(l => l.Id == move.FromId && l.ArchivedAt == null, ct))
                {
                    return BoardResults.Conflict("The list the card came from is archived.");
                }

                card.ListId = move.FromId;
                card.Position = move.FromPosition;
            }

            if (changes.CompletedAt is { } c)
            {
                card.CompletedAt = c.Before;
            }

            if (changes.DueAt is { } d)
            {
                card.DueAt = d.Before;
            }

            if (changes.ArchivedAt is { } a)
            {
                card.ArchivedAt = a.Before;
            }

            if (changes.Priority is { } p)
            {
                card.Priority = p.Before;
            }

            await RevertLinksAsync(db, card.Id, changes, ct);

            run.UndoneAt = clock.GetUtcNow();
            var rule = await db.AutomationRules.AsNoTracking().Where(r => r.Id == run.RuleId).Select(r => r.Name).FirstOrDefaultAsync(ct);
            var prefix = await db.Boards.AsNoTracking().Where(b => b.Id == boardId).Select(b => b.KeyPrefix).FirstAsync(ct);

            // The marker keeps this change from triggering automations of its own.
            var data = new JsonObject
            {
                ["rule"] = rule,
                ["card"] = new JsonObject { ["key"] = Card.Key(prefix, card.Number), ["title"] = card.Title },
                [AutomationMarker.Property] = new JsonObject { ["ruleId"] = run.RuleId, ["name"] = rule, ["runId"] = run.Id },
            };
            db.Activity.Add(new ActivityEntry
            {
                Type = ActivityTypes.AutomationUndone,
                BoardId = boardId,
                CardId = card.Id,
                ActorId = user.Id,
                Data = data.ToJsonString(AutomationSpec.Json),
                CreatedAt = clock.GetUtcNow(),
            });

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return BoardResults.Conflict("The card was changed by someone else. Reload and try again.");
            }

            return TypedResults.Ok((await CardSummaries.FindAsync(db, boardId, card.Id, ct))!);
        }

        private static async Task RevertLinksAsync(AppDbContext db, Guid cardId, RunChanges changes, CancellationToken ct)
        {
            db.CardLabels.RemoveRange(await db.CardLabels.Where(l => l.CardId == cardId && changes.LabelsAdded.Contains(l.LabelId)).ToListAsync(ct));
            var labels = await db.Labels.Where(l => changes.LabelsRemoved.Contains(l.Id)).Select(l => l.Id).ToListAsync(ct);
            var present = await db.CardLabels.Where(l => l.CardId == cardId).Select(l => l.LabelId).ToListAsync(ct);
            db.CardLabels.AddRange(labels.Except(present).Select(id => new CardLabel { CardId = cardId, LabelId = id }));

            db.CardAssignees.RemoveRange(await db.CardAssignees.Where(a => a.CardId == cardId && changes.AssigneesAdded.Contains(a.UserId)).ToListAsync(ct));
            var boardId = await db.Cards.Where(c => c.Id == cardId).Select(c => c.BoardId).FirstAsync(ct);
            var members = await db.BoardMembers.Where(m => m.BoardId == boardId && changes.AssigneesRemoved.Contains(m.UserId)).Select(m => m.UserId).ToListAsync(ct);
            var assigned = await db.CardAssignees.Where(a => a.CardId == cardId).Select(a => a.UserId).ToListAsync(ct);
            db.CardAssignees.AddRange(members.Except(assigned).Select(id => new CardAssignee { CardId = cardId, UserId = id }));

            db.ChecklistItems.RemoveRange(await db.ChecklistItems.Where(i => i.CardId == cardId && changes.ChecklistItems.Contains(i.Id)).ToListAsync(ct));
            if (changes.CommentId is { } commentId)
            {
                db.CommentMentions.RemoveRange(await db.CommentMentions.Where(m => m.CommentId == commentId).ToListAsync(ct));
                db.Comments.RemoveRange(await db.Comments.Where(x => x.Id == commentId && x.CardId == cardId).ToListAsync(ct));
            }
        }
    }
}
