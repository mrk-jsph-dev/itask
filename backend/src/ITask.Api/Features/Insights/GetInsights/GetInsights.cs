using System.Text.Json;
using FluentValidation;
using ITask.Api.Common.Activity;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Cards;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Insights;
using ITask.Api.Common.Persistence;
using ITask.Api.Common.Security;
using ITask.Api.Common.Validation;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Insights.GetInsights;

/// <summary>
/// How work flows on a board over the last <c>days</c> local days (time zone <c>tz</c>, default UTC):
/// cards done and created per day, cycle time, median stay per list, the oldest open cards and WIP.
/// Computed on request from cards and their <c>card.created</c> / <c>card.moved</c> activity. Any member may read.
/// Archived cards count only when they were completed inside the range. The last active list by
/// position is the final list and is left out of aging.
/// </summary>
public static class GetInsights
{
    public static readonly IReadOnlySet<int> Ranges = new HashSet<int> { 30, 60, 90 };

    public const int DefaultDays = 30;

    public const int AgingTop = 10;

    public sealed record Request(int? Days, string? Tz);

    public sealed record DayCount(DateOnly Date, int Count);

    /// <summary>Days from creation to completion for cards completed in the range; nulls when none.</summary>
    public sealed record CycleTime(int Count, double? MedianDays, double? P85Days);

    public sealed record ListTime(Guid ListId, string Name, string? Emoji, double? MedianDays, int Samples);

    public sealed record AgingCard(Guid Id, string Key, string Title, Guid ListId, string ListName, double Days);

    public sealed record ListWip(Guid ListId, string Name, string? Emoji, int Open, int? Limit);

    public sealed record Response(
        int Days,
        DateOnly From,
        DateOnly To,
        IReadOnlyList<DayCount> Throughput,
        IReadOnlyList<DayCount> Created,
        CycleTime CycleTime,
        IReadOnlyList<ListTime> TimeInList,
        IReadOnlyList<AgingCard> Aging,
        IReadOnlyList<ListWip> Wip);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.Days)
                .Must(d => d is null || Ranges.Contains(d.Value))
                .WithMessage("Use 30, 60 or 90 days.");
            RuleFor(r => r.Tz)
                .Must(v => v is null || TimeZoneInfo.TryFindSystemTimeZoneById(v, out _))
                .WithMessage("Unknown time zone.");
        }
    }

    private sealed record CardRow(
        Guid Id, int Number, string Title, Guid ListId, DateTimeOffset CreatedAt,
        DateTimeOffset? CompletedAt, DateTimeOffset? ArchivedAt, DateTimeOffset ListEnteredAt);

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapGet("/boards/{boardId:guid}/insights", Handle)
                .WithTags("Insights")
                .RequireAuthorization()
                .RequireRateLimiting(RateLimits.Search)
                .WithValidation<Request>();

        private static async Task<Results<Ok<Response>, NotFound>> Handle(
            Guid boardId,
            [AsParameters] Request request,
            AppDbContext db,
            IBoardAccess access,
            TimeProvider clock,
            CancellationToken ct)
        {
            if (await access.GetRoleAsync(boardId, ct) is null)
            {
                return TypedResults.NotFound();
            }

            var days = request.Days ?? DefaultDays;
            var zone = request.Tz is { } tz && TimeZoneInfo.TryFindSystemTimeZoneById(tz, out var z) ? z : TimeZoneInfo.Utc;
            var now = clock.GetUtcNow();
            var to = InsightMath.LocalDate(now, zone);
            var from = to.AddDays(1 - days);
            var rangeStart = DueBuckets.Local(from.ToDateTime(TimeOnly.MinValue), zone);
            bool InRange(DateTimeOffset? at) => at is { } a && a >= rangeStart && a <= now;

            var prefix = await db.Boards.AsNoTracking().Where(b => b.Id == boardId).Select(b => b.KeyPrefix).FirstAsync(ct);
            var lists = await db.Lists.AsNoTracking()
                .Where(l => l.BoardId == boardId && l.ArchivedAt == null)
                .OrderBy(l => l.Position)
                .Select(l => new { l.Id, l.Name, l.Emoji, l.WipLimit })
                .ToListAsync(ct);
            var cards = await db.Cards.AsNoTracking()
                .Where(c => c.BoardId == boardId)
                .Select(c => new CardRow(c.Id, c.Number, c.Title, c.ListId, c.CreatedAt, c.CompletedAt, c.ArchivedAt, c.ListEnteredAt))
                .ToListAsync(ct);
            var entries = await db.Activity.AsNoTracking()
                .Where(a => a.BoardId == boardId && a.CardId != null
                    && (a.Type == ActivityTypes.CardCreated || a.Type == ActivityTypes.CardMoved))
                .Select(a => new { CardId = a.CardId!.Value, a.Type, a.Data, a.CreatedAt })
                .ToListAsync(ct);

            // Entries written before list ids were recorded carry names only; active list names are unique.
            var byName = lists.ToDictionary(l => l.Name, l => l.Id, StringComparer.OrdinalIgnoreCase);
            Guid? Resolve(JsonElement data, string idProperty, string nameProperty)
            {
                if (data.TryGetProperty(idProperty, out var id) && id.TryGetGuid(out var guid))
                {
                    return guid;
                }

                return data.TryGetProperty(nameProperty, out var name) && name.ValueKind == JsonValueKind.String
                    && byName.TryGetValue(name.GetString()!, out var match) ? match : null;
            }

            var createdIn = new Dictionary<Guid, Guid?>();
            var moves = new Dictionary<Guid, List<ListMove>>();
            foreach (var entry in entries)
            {
                using var json = JsonDocument.Parse(entry.Data);
                var data = json.RootElement;
                if (entry.Type == ActivityTypes.CardCreated)
                {
                    createdIn[entry.CardId] = Resolve(data, "listId", "list");
                }
                else if (data.TryGetProperty("toList", out _))
                {
                    // Reorders within a list record only "list" and aren't stays.
                    if (!moves.TryGetValue(entry.CardId, out var list))
                    {
                        moves[entry.CardId] = list = [];
                    }

                    list.Add(new ListMove(entry.CreatedAt, Resolve(data, "fromListId", "fromList"), Resolve(data, "toListId", "toList")));
                }
            }

            var completedInRange = cards.Where(c => InRange(c.CompletedAt)).ToList();
            var throughput = InsightMath.PerDay(completedInRange.Select(c => c.CompletedAt!.Value), from, to, zone);
            var created = InsightMath.PerDay(
                cards.Where(c => c.ArchivedAt == null || InRange(c.CompletedAt)).Select(c => c.CreatedAt), from, to, zone);

            var cycle = completedInRange.Select(c => (c.CompletedAt!.Value - c.CreatedAt).TotalDays).ToList();
            var cycleTime = new CycleTime(
                cycle.Count,
                InsightMath.Round(InsightMath.Percentile(cycle, 0.5)),
                InsightMath.Round(InsightMath.Percentile(cycle, 0.85)));

            var stays = cards
                .Where(c => c.ArchivedAt == null || InRange(c.CompletedAt))
                .SelectMany(c => InsightMath.Segments(
                    new SegmentCard(c.ListId, c.CreatedAt, c.ListEnteredAt, c.CompletedAt, c.ArchivedAt),
                    createdIn.GetValueOrDefault(c.Id),
                    moves.GetValueOrDefault(c.Id) ?? [],
                    now))
                .Where(s => s.End >= rangeStart)
                .ToLookup(s => s.ListId, s => s.Days);
            var timeInList = lists
                .Select(l => new ListTime(
                    l.Id, l.Name, l.Emoji, InsightMath.Round(InsightMath.Percentile(stays[l.Id], 0.5)), stays[l.Id].Count()))
                .ToList();

            var listNames = lists.ToDictionary(l => l.Id, l => l.Name);
            var finalListId = lists.Count > 0 ? lists[^1].Id : (Guid?)null;
            var open = cards.Where(c => c.ArchivedAt == null && c.CompletedAt == null && listNames.ContainsKey(c.ListId)).ToList();
            var aging = open
                .Where(c => c.ListId != finalListId)
                .OrderBy(c => c.ListEnteredAt)
                .ThenBy(c => c.Number)
                .Take(AgingTop)
                .Select(c => new AgingCard(
                    c.Id, Card.Key(prefix, c.Number), c.Title, c.ListId, listNames[c.ListId],
                    InsightMath.Round((now - c.ListEnteredAt).TotalDays)!.Value))
                .ToList();
            var openByList = open.CountBy(c => c.ListId).ToDictionary();
            var wip = lists
                .Select(l => new ListWip(l.Id, l.Name, l.Emoji, openByList.GetValueOrDefault(l.Id), l.WipLimit))
                .ToList();

            return TypedResults.Ok(new Response(
                days,
                from,
                to,
                [.. throughput.Select(d => new DayCount(d.Date, d.Count))],
                [.. created.Select(d => new DayCount(d.Date, d.Count))],
                cycleTime,
                timeInList,
                aging,
                wip));
        }
    }
}
