using System.Text.Json;
using FluentValidation;
using ITask.Api.Common.Activity;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Automations;
using ITask.Api.Common.Cards;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using ITask.Api.Common.Validation;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Automations.DraftFromRecording;

/// <summary>
/// "Show me once": turns what the caller just did to one card into a rule draft. Editor or above.
/// The first move into a list (or the card's creation, or completing it) becomes the trigger; the
/// steps after it become up to three actions. Reads the caller's own activity on the card since
/// <c>Since</c> (at most 30 minutes back, 20 entries). 422 when the steps don't make a rule.
/// Nothing is saved.
/// </summary>
public static class DraftFromRecording
{
    public static readonly TimeSpan MaxLength = TimeSpan.FromMinutes(30);

    private const int MaxEntries = 20;

    public sealed record Request(Guid CardId, DateTimeOffset Since);

    public sealed record Response(string Name, TriggerSpec Trigger, IReadOnlyList<ActionSpec> Actions);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.CardId).NotEmpty();
        }
    }

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapPost("/boards/{boardId:guid}/automations/from-recording", Handle)
                .WithTags("Automations")
                .RequireAuthorization()
                .WithValidation<Request>();

        private static async Task<Results<Ok<Response>, ProblemHttpResult>> Handle(
            Guid boardId, Request request, AppDbContext db, IBoardAccess access, ICurrentUser user, TimeProvider clock, CancellationToken ct)
        {
            if (await AutomationGuard.CheckAsync(boardId, BoardRole.Editor, false, db, access, ct) is { } denied)
            {
                return denied;
            }

            if (!await CardGuard.CardExistsAsync(db, boardId, request.CardId, ct))
            {
                return CardGuard.NotFound();
            }

            var now = clock.GetUtcNow();
            var since = request.Since < now - MaxLength ? now - MaxLength : request.Since;
            var userId = user.Id;
            var entries = (await db.Activity.AsNoTracking()
                    .Where(a => a.BoardId == boardId && a.CardId == request.CardId && a.ActorId == userId && a.CreatedAt >= since)
                    .OrderBy(a => a.Id)
                    .Take(MaxEntries)
                    .ToListAsync(ct))
                .Where(e => !AutomationMarker.Has(e.Data))
                .ToList();

            var board = await AutomationBoards.LoadAsync(db, boardId, ct);
            var steps = entries.SelectMany(e => Steps(e, board, userId)).ToList();
            var triggerIndex = steps.FindIndex(s => s.Trigger is not null);
            if (triggerIndex < 0)
            {
                return Unprocessable("Start by moving the card into a list, creating it or completing it.");
            }

            var trigger = steps[triggerIndex].Trigger!;
            var actions = new List<ActionSpec>();
            foreach (var action in steps.Skip(triggerIndex + 1).Select(s => s.Action).OfType<ActionSpec>())
            {
                // The latest step of the same kind (and target) wins; checklist items are merged.
                var same = actions.FindIndex(a => a.Type == action.Type && (a.LabelId == action.LabelId || action.Type is AutomationCatalog.DoChecklist));
                if (same >= 0 && action.Type == AutomationCatalog.DoChecklist)
                {
                    actions[same] = actions[same] with { Items = [.. actions[same].Items!, .. action.Items!] };
                }
                else if (same >= 0)
                {
                    actions[same] = action;
                }
                else
                {
                    actions.Add(action);
                }
            }

            actions = [.. actions.Take(AutomationRule.MaxActions)
                .Select(a => a.Items is { Count: > AutomationCatalog.MaxChecklistItems } items ? a with { Items = [.. items.Take(AutomationCatalog.MaxChecklistItems)] } : a)];
            if (actions.Count == 0)
            {
                return Unprocessable("Do at least one more step after that, such as adding a label or assigning someone.");
            }

            var name = trigger.ListId is { } listId && board.Lists.TryGetValue(listId, out var list)
                ? $"{list} routine"
                : "My routine";
            return TypedResults.Ok(new Response(name, trigger, actions));
        }

        private sealed record Step(TriggerSpec? Trigger, ActionSpec? Action);

        private static IEnumerable<Step> Steps(ActivityEntry entry, BoardContext board, Guid userId)
        {
            using var json = JsonDocument.Parse(entry.Data);
            var data = json.RootElement;
            switch (entry.Type)
            {
                case ActivityTypes.CardMoved or ActivityTypes.CardCreated:
                {
                    var listId = AutomationHistory.Events(entry, board)
                        .FirstOrDefault(e => e.Type == AutomationCatalog.CardEntered)?.ListId;
                    if (listId is { } id)
                    {
                        yield return new Step(new TriggerSpec(AutomationCatalog.CardEntered, id), new ActionSpec(AutomationCatalog.DoMove, ListId: id));
                    }

                    break;
                }

                case ActivityTypes.CardCompleted:
                    yield return new Step(new TriggerSpec(AutomationCatalog.CardCompleted), new ActionSpec(AutomationCatalog.DoComplete));
                    break;
                case ActivityTypes.CardReopened:
                    yield return new Step(null, new ActionSpec(AutomationCatalog.DoReopen));
                    break;
                case ActivityTypes.CardLabelsChanged:
                    foreach (var e in AutomationHistory.Events(entry, board))
                    {
                        yield return new Step(null, new ActionSpec(
                            e.Type == AutomationCatalog.LabelAdded ? AutomationCatalog.DoAddLabel : AutomationCatalog.DoRemoveLabel, LabelId: e.LabelId));
                    }

                    break;
                case ActivityTypes.CardAssigneesChanged:
                    foreach (var name in Names(data, "added"))
                    {
                        // Activity keeps display names only; the person recording wins a tie.
                        var member = board.Members.TryGetValue(userId, out var me) && me.Name == name
                            ? new KeyValuePair<Guid, (string Name, BoardRole Role)>(userId, me)
                            : board.Members.FirstOrDefault(m => m.Value.Name == name);
                        if (member.Key == userId)
                        {
                            yield return new Step(null, new ActionSpec(AutomationCatalog.DoAssign, Who: "actor"));
                        }
                        else if (member.Key != Guid.Empty && member.Value.Role >= BoardRole.Editor)
                        {
                            yield return new Step(null, new ActionSpec(AutomationCatalog.DoAssign, Who: "member", UserId: member.Key));
                        }
                    }

                    break;
                case ActivityTypes.CardUpdated:
                    var fields = Names(data, "fields").ToList();
                    if (fields.Contains("priority") && data.TryGetProperty("priority", out var p)
                        && Enum.TryParse<CardPriority>(p.GetString(), out var priority))
                    {
                        yield return new Step(null, new ActionSpec(AutomationCatalog.DoPriority, Priority: priority));
                    }

                    if (fields.Contains("dueAt") && data.TryGetProperty("dueAt", out var due) && due.TryGetDateTimeOffset(out var at))
                    {
                        var days = Math.Clamp((int)Math.Round((at - entry.CreatedAt).TotalDays), 0, 365);
                        yield return new Step(null, new ActionSpec(AutomationCatalog.DoDue, Days: days));
                    }

                    break;
                case ActivityTypes.ChecklistAdded when data.TryGetProperty("item", out var item) && item.GetString() is { Length: > 0 } text:
                    yield return new Step(null, new ActionSpec(AutomationCatalog.DoChecklist, Items: [text]));
                    break;
            }
        }

        private static IEnumerable<string> Names(JsonElement data, string property) =>
            data.TryGetProperty(property, out var values) && values.ValueKind == JsonValueKind.Array
                ? [.. values.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!)]
                : [];

        private static ProblemHttpResult Unprocessable(string title) =>
            TypedResults.Problem(title: title, statusCode: StatusCodes.Status422UnprocessableEntity);
    }
}
