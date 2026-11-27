using System.Text.RegularExpressions;
using FluentValidation;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Cards;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using ITask.Api.Common.Validation;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Cards.QueryCards;

/// <summary>
/// The board's active cards (on active lists) matching a filter, sorted, for the List and Calendar
/// views. Any member may read. Multi-value parameters are comma-separated and match any value;
/// different parameters must all match. Due buckets use the <c>tz</c> time zone (default UTC).
/// </summary>
public static partial class QueryCards
{
    public const int MaxItems = 1000;

    public const int MaxQueryLength = 100;

    public const int MaxValues = 50;

    public static readonly TimeSpan MaxRange = TimeSpan.FromDays(62);

    /// <summary>Assignee value for cards with nobody assigned.</summary>
    public const string Unassigned = "none";

    public enum SortBy
    {
        Position,
        Due,
        Priority,
        Created,
        Title,
    }

    public sealed record Request(
        string? Labels,
        string? Assignees,
        string? Due,
        string? Priority,
        string? Q,
        bool? Completed,
        DateTimeOffset? DueFrom,
        DateTimeOffset? DueTo,
        string? Sort,
        string? Tz,
        string? Blocked = null);

    public sealed record Response(IReadOnlyList<CardSummary> Items, bool Truncated);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.Labels).Must(v => TryGuids(v, out _)).WithMessage("Use comma-separated label ids.");
            RuleFor(r => r.Assignees)
                .Must(v => TryAssignees(v, out _, out _))
                .WithMessage($"Use comma-separated user ids or \"{Unassigned}\".");
            RuleFor(r => r.Due)
                .Must(v => TryEnums<DueBucket>(v, out _))
                .WithMessage("Use overdue, today, week or none.");
            RuleFor(r => r.Priority)
                .Must(v => TryEnums<CardPriority>(v, out _))
                .WithMessage("Use None, Low, Medium, High or Urgent.");
            RuleFor(r => r.Q).MaximumLength(MaxQueryLength);
            RuleFor(r => r.Sort)
                .Must(v => v is null || TryEnum<SortBy>(v, out _))
                .WithMessage("Use position, due, priority, created or title.");
            RuleFor(r => r.Blocked).Must(v => v is null or "yes" or "no").WithMessage("Use yes or no.");
            RuleFor(r => r.Tz).Must(v => v is null || TryZone(v, out _)).WithMessage("Unknown time zone.");
            RuleFor(r => r.DueTo)
                .Must((r, to) => r.DueFrom <= to && to - r.DueFrom <= MaxRange)
                .When(r => r.DueFrom is not null && r.DueTo is not null)
                .WithMessage($"dueTo must follow dueFrom, at most {MaxRange.TotalDays} days later.");
        }
    }

    [GeneratedRegex(@"^([A-Za-z]{2,5})-(\d{1,9})$", RegexOptions.CultureInvariant)]
    private static partial Regex CardKeyPattern();

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapGet("/boards/{boardId:guid}/cards", Handle)
                .WithTags("Cards")
                .RequireAuthorization()
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

            TryGuids(request.Labels, out var labelIds);
            TryAssignees(request.Assignees, out var userIds, out var unassigned);
            TryEnums<DueBucket>(request.Due, out var buckets);
            TryEnums<CardPriority>(request.Priority, out var priorities);
            var sort = request.Sort is null ? SortBy.Position : TryEnum<SortBy>(request.Sort, out var s) ? s : SortBy.Position;
            var zone = request.Tz is null ? TimeZoneInfo.Utc : TryZone(request.Tz, out var z) ? z : TimeZoneInfo.Utc;
            var hideCompleted = request.Completed == false;
            var anyAssignee = userIds.Count > 0 || unassigned;

            var term = string.IsNullOrWhiteSpace(request.Q) ? null : request.Q.Trim().ToLowerInvariant();
            int? keyNumber = null;
            if (term is not null && CardKeyPattern().Match(term) is { Success: true } key)
            {
                var prefix = await db.Boards.Where(b => b.Id == boardId).Select(b => b.KeyPrefix).FirstAsync(ct);
                if (string.Equals(key.Groups[1].Value, prefix, StringComparison.OrdinalIgnoreCase))
                {
                    keyNumber = int.Parse(key.Groups[2].ValueSpan, System.Globalization.CultureInfo.InvariantCulture);
                }
            }

            var cards = await CardSummaries.QueryAsync(
                db,
                boardId,
                c => c.ArchivedAt == null
                    && c.List!.ArchivedAt == null
                    && (!hideCompleted || c.CompletedAt == null)
                    && (labelIds.Count == 0 || c.Labels.Any(l => labelIds.Contains(l.LabelId)))
                    && (!anyAssignee
                        || c.Assignees.Any(a => userIds.Contains(a.UserId))
                        || (unassigned && !c.Assignees.Any()))
                    && (priorities.Count == 0 || priorities.Contains(c.Priority))
                    && (term == null || c.SearchTitle.Contains(term) || c.Number == keyNumber),
                ct);

            // DateTimeOffset filters and the sorts run in memory: SQLite can't translate them, and
            // Priority is stored by name.
            var now = clock.GetUtcNow();
            IEnumerable<CardSummary> matches = cards;
            if (buckets.Count > 0)
            {
                matches = matches.Where(c => buckets.Any(b => DueBuckets.Matches(b, c.DueAt, c.CompletedAt, now, zone)));
            }

            if (request.Blocked is { } blocked)
            {
                matches = matches.Where(c => c.Blocked == (blocked == "yes"));
            }

            if (request.DueFrom is { } from)
            {
                matches = matches.Where(c => c.DueAt >= from);
            }

            if (request.DueTo is { } to)
            {
                matches = matches.Where(c => c.DueAt < to);
            }

            var listOrder = await db.Lists
                .AsNoTracking()
                .Where(l => l.BoardId == boardId && l.ArchivedAt == null)
                .Select(l => new { l.Id, l.Position })
                .ToDictionaryAsync(l => l.Id, l => l.Position, ct);

            var sorted = Sort(matches, sort, listOrder).ToList();
            return TypedResults.Ok(new Response([.. sorted.Take(MaxItems)], sorted.Count > MaxItems));
        }
    }

    /// <summary>Due soonest first (no date last), priority highest first, created newest first; ties by number.</summary>
    private static IEnumerable<CardSummary> Sort(
        IEnumerable<CardSummary> cards, SortBy sort, Dictionary<Guid, double> listOrder) =>
        sort switch
        {
            SortBy.Due => cards.OrderBy(c => c.DueAt is null).ThenBy(c => c.DueAt).ThenBy(c => c.Number),
            SortBy.Priority => cards.OrderByDescending(c => c.Priority).ThenBy(c => c.Number),
            SortBy.Created => cards.OrderByDescending(c => c.Number),
            SortBy.Title => cards
                .OrderBy(c => c.Title, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Number),
            _ => cards
                .OrderBy(c => listOrder.GetValueOrDefault(c.ListId))
                .ThenBy(c => c.Position),
        };

    private static string[] Split(string? value) =>
        (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool TryGuids(string? value, out List<Guid> ids)
    {
        ids = [];
        foreach (var part in Split(value))
        {
            if (!Guid.TryParse(part, out var id))
            {
                return false;
            }

            ids.Add(id);
        }

        return ids.Count <= MaxValues;
    }

    private static bool TryAssignees(string? value, out List<Guid> ids, out bool unassigned)
    {
        unassigned = Split(value).Any(p => string.Equals(p, Unassigned, StringComparison.OrdinalIgnoreCase));
        return TryGuids(
            string.Join(',', Split(value).Where(p => !string.Equals(p, Unassigned, StringComparison.OrdinalIgnoreCase))),
            out ids);
    }

    private static bool TryEnum<T>(string value, out T result)
        where T : struct, Enum =>
        Enum.TryParse(value, ignoreCase: true, out result) && Enum.IsDefined(result) && !int.TryParse(value, out _);

    private static bool TryEnums<T>(string? value, out List<T> values)
        where T : struct, Enum
    {
        values = [];
        foreach (var part in Split(value))
        {
            if (!TryEnum<T>(part, out var parsed))
            {
                return false;
            }

            values.Add(parsed);
        }

        return true;
    }

    private static bool TryZone(string id, out TimeZoneInfo zone) =>
        TimeZoneInfo.TryFindSystemTimeZoneById(id, out zone!);
}
