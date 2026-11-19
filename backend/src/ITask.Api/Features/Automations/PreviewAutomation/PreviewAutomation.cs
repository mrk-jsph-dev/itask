using FluentValidation;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Automations;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using ITask.Api.Common.Security;
using ITask.Api.Common.Validation;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Automations.PreviewAutomation;

/// <summary>
/// Replays an automation that isn't saved yet. Editor or above. Instant triggers are matched against
/// the board's last 30 days of activity (at most <see cref="MaxEntries"/> entries); conditions and
/// doorway requirements are checked against the cards as they are now. Time-based triggers report the
/// cards they would act on now. Nothing is changed.
/// </summary>
public static class PreviewAutomation
{
    public const int MaxEntries = 2_000;

    public const int Days = 30;

    public sealed record Request(
        AutomationKind Kind,
        Guid? ListId,
        DoorwayDirection? Direction,
        DoorwayMode? Mode,
        TriggerSpec? Trigger,
        IReadOnlyList<ConditionSpec>? Conditions,
        IReadOnlyList<ActionSpec>? Actions);

    public sealed record Sample(Guid CardId, string Key, string Title);

    /// <param name="Basis"><c>history</c> (last 30 days) or <c>now</c> (cards it would act on now).</param>
    public sealed record Response(int Count, IReadOnlyList<Sample> Samples, string Basis);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.Kind).IsInEnum();
        }
    }

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapPost("/boards/{boardId:guid}/automations/preview", Handle)
                .WithTags("Automations")
                .RequireAuthorization()
                .RequireRateLimiting(RateLimits.Search)
                .WithValidation<Request>();

        private static async Task<Results<Ok<Response>, ValidationProblem, ProblemHttpResult>> Handle(
            Guid boardId, Request request, AppDbContext db, IBoardAccess access, TimeProvider clock, CancellationToken ct)
        {
            if (await AutomationGuard.CheckAsync(boardId, BoardRole.Editor, false, db, access, ct) is { } denied)
            {
                return denied;
            }

            var (spec, errors) = await AutomationGuard.ValidateAsync(
                db, boardId, request.Kind, request.ListId, request.Direction, request.Mode,
                request.Trigger, request.Conditions, request.Actions, aiAllowed: true, ct);
            if (errors is not null)
            {
                return TypedResults.ValidationProblem(errors);
            }

            var now = clock.GetUtcNow();
            var board = await AutomationBoards.LoadAsync(db, boardId, ct);
            var doorway = request.Kind == AutomationKind.Doorway;

            // A card counts when its rule conditions hold, or when it misses a doorway's requirements.
            var cache = new Dictionary<Guid, bool>();
            async Task<bool> CountsAsync(Guid cardId)
            {
                if (cache.TryGetValue(cardId, out var known))
                {
                    return known;
                }

                var card = await db.Cards.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cardId && c.ArchivedAt == null, ct);
                var counts = false;
                if (card is not null)
                {
                    var state = await CardState.LoadAsync(db, card, ct);
                    counts = doorway
                        ? spec!.Conditions.Count == 0 || spec.Conditions.Any(c => !state.Meets(c))
                        : spec!.Conditions.All(state.Meets);
                }

                return cache[cardId] = counts;
            }

            List<Guid> hits = [];
            string basis;
            if (AutomationCatalog.IsHourly(spec!.Trigger.Type))
            {
                basis = "now";
                foreach (var (cardId, _) in (await AutomationSchedule.CandidatesAsync(db, boardId, spec.Trigger, now, ct)).Take(200))
                {
                    if (await CountsAsync(cardId))
                    {
                        hits.Add(cardId);
                    }
                }
            }
            else
            {
                basis = "history";
                var types = AutomationHistory.Types.ToList();
                var entries = (await db.Activity.AsNoTracking()
                        .Where(a => a.BoardId == boardId && a.CardId != null && types.Contains(a.Type) && a.CreatedAt >= now.AddDays(-Days))
                        .OrderByDescending(a => a.Id)
                        .Take(MaxEntries)
                        .ToListAsync(ct))
                    .SelectMany(e => AutomationHistory.Events(e, board))
                    .Where(e => AutomationHistory.Matches(spec.Trigger, e))
                    .ToList();
                foreach (var e in entries)
                {
                    if (await CountsAsync(e.CardId))
                    {
                        hits.Add(e.CardId);
                    }
                }
            }

            var sampleIds = hits.Distinct().Take(3).ToList();
            var prefix = await db.Boards.AsNoTracking().Where(b => b.Id == boardId).Select(b => b.KeyPrefix).FirstAsync(ct);
            var samples = (await db.Cards.AsNoTracking()
                    .Where(c => sampleIds.Contains(c.Id))
                    .Select(c => new { c.Id, c.Number, c.Title })
                    .ToListAsync(ct))
                .Select(c => new Sample(c.Id, Card.Key(prefix, c.Number), c.Title))
                .ToList();
            return TypedResults.Ok(new Response(hits.Count, samples, basis));
        }
    }
}
