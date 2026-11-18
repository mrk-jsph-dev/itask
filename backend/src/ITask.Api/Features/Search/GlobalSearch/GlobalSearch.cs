using System.Globalization;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using FluentValidation;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using ITask.Api.Common.Security;
using ITask.Api.Common.Validation;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Search.GlobalSearch;

/// <summary>
/// Case-insensitive substring search over board names, card titles and comment bodies on the
/// caller's boards. Archived boards, lists and cards are left out. A query shaped like a card key
/// (<c>ITK-42</c>) also returns the cards with that key first, flagged <see cref="CardHit.KeyMatch"/>.
/// </summary>
public static partial class GlobalSearch
{
    public const int MinLength = 2;

    public const int MaxLength = 100;

    public const int DefaultLimit = 20;

    public const int MaxLimit = 50;

    private const int SnippetBefore = 40;

    private const int SnippetAfter = 80;

    public sealed record Request(string? Q, int? Limit);

    public sealed record BoardHit(Guid Id, string Name, string? Emoji, string Color);

    public sealed record CardHit(
        Guid Id, Guid BoardId, string Key, string Title, string BoardName, string? BoardEmoji,
        string ListName, DateTimeOffset? CompletedAt, bool KeyMatch);

    public sealed record CommentHit(
        Guid Id, Guid CardId, Guid BoardId, string CardKey, string CardTitle, string AuthorName,
        DateTimeOffset CreatedAt, string Snippet);

    public sealed record Response(
        IReadOnlyList<BoardHit> Boards, IReadOnlyList<CardHit> Cards, IReadOnlyList<CommentHit> Comments);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.Q)
                .Cascade(CascadeMode.Stop)
                .NotEmpty()
                .Must(q => q!.Trim().Length is >= MinLength and <= MaxLength)
                .WithMessage($"Search for {MinLength} to {MaxLength} characters.");
            RuleFor(r => r.Limit).InclusiveBetween(1, MaxLimit).When(r => r.Limit is not null);
        }
    }

    private sealed record CardRow(
        Guid Id, Guid BoardId, string KeyPrefix, int Number, string Title, string BoardName, string? BoardEmoji,
        string ListName, DateTimeOffset? CompletedAt)
    {
        public CardHit ToHit(bool keyMatch) => new(
            Id, BoardId, Card.Key(KeyPrefix, Number), Title, BoardName, BoardEmoji, ListName, CompletedAt, keyMatch);
    }

    private static readonly Expression<Func<Card, CardRow>> CardProjection = c => new CardRow(
        c.Id, c.BoardId, c.Board!.KeyPrefix, c.Number, c.Title, c.Board.Name, c.Board.Emoji, c.List!.Name, c.CompletedAt);

    [GeneratedRegex(@"^([A-Za-z]{2,5})-(\d{1,9})$", RegexOptions.CultureInvariant)]
    private static partial Regex CardKeyPattern();

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapGet("/search", Handle)
                .WithTags("Search")
                .RequireAuthorization()
                .RequireRateLimiting(RateLimits.Search)
                .WithValidation<Request>();

        private static async Task<Ok<Response>> Handle(
            [AsParameters] Request request, AppDbContext db, ICurrentUser user, CancellationToken ct)
        {
            var query = request.Q!.Trim();
            var term = query.ToLowerInvariant();
            var limit = request.Limit ?? DefaultLimit;
            var userId = user.Id;

            var memberBoards = db.BoardMembers.Where(m => m.UserId == userId).Select(m => m.BoardId);
            var cards = db.Cards.AsNoTracking().Where(c =>
                memberBoards.Contains(c.BoardId)
                && c.ArchivedAt == null
                && c.List!.ArchivedAt == null
                && c.Board!.ArchivedAt == null);

            var boards = await db.Boards
                .AsNoTracking()
                .Where(b => memberBoards.Contains(b.Id) && b.ArchivedAt == null && b.SearchName.Contains(term))
                .OrderBy(b => b.SearchName.StartsWith(term) ? 0 : 1)
                .ThenBy(b => b.SearchName)
                .Take(limit)
                .Select(b => new BoardHit(b.Id, b.Name, b.Emoji, b.Color))
                .ToListAsync(ct);

            var keyRows = await KeyMatchesAsync(cards, query, limit, ct);
            var keyIds = keyRows.Select(c => c.Id).ToList();
            var titleRows = await cards
                .Where(c => c.SearchTitle.Contains(term) && !keyIds.Contains(c.Id))
                .OrderBy(c => c.SearchTitle.StartsWith(term) ? 0 : 1)
                .ThenBy(c => c.SearchTitle)
                .Take(limit - keyRows.Count)
                .Select(CardProjection)
                .ToListAsync(ct);

            var commentRows = await db.Comments
                .AsNoTracking()
                .Where(m => cards.Any(c => c.Id == m.CardId) && m.SearchBody.Contains(term))
                .OrderByDescending(m => m.CreatedAt)
                .Take(limit)
                .Select(m => new
                {
                    m.Id,
                    m.CardId,
                    m.Card!.BoardId,
                    m.Card.Board!.KeyPrefix,
                    m.Card.Number,
                    m.Card.Title,
                    AuthorName = m.Author!.DisplayName,
                    m.CreatedAt,
                    m.Body,
                    m.SearchBody,
                })
                .ToListAsync(ct);

            var comments = commentRows
                .Select(m => new CommentHit(
                    m.Id, m.CardId, m.BoardId, Card.Key(m.KeyPrefix, m.Number), m.Title, m.AuthorName, m.CreatedAt,
                    Snippet(m.Body, m.SearchBody.IndexOf(term, StringComparison.Ordinal), term.Length)))
                .ToList();

            var hits = keyRows.Select(c => c.ToHit(keyMatch: true)).Concat(titleRows.Select(c => c.ToHit(keyMatch: false)));
            return TypedResults.Ok(new Response(boards, [.. hits], comments));
        }

        private static async Task<List<CardRow>> KeyMatchesAsync(
            IQueryable<Card> cards, string query, int limit, CancellationToken ct)
        {
            var match = CardKeyPattern().Match(query);
            if (!match.Success
                || !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            {
                return [];
            }

            var prefix = match.Groups[1].Value.ToUpperInvariant();
            return await cards
                .Where(c => c.Board!.KeyPrefix == prefix && c.Number == number)
                .OrderBy(c => c.Board!.SearchName)
                .Take(limit)
                .Select(CardProjection)
                .ToListAsync(ct);
        }
    }

    /// <summary>
    /// Single-line excerpt of <paramref name="body"/> around the match at <paramref name="index"/>,
    /// with ellipses where text was cut.
    /// </summary>
    internal static string Snippet(string body, int index, int length)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (index < 0)
        {
            index = 0;
            length = 0;
        }

        var start = Math.Max(0, index - SnippetBefore);
        var end = Math.Min(body.Length, index + length + SnippetAfter);
        var text = string.Join(' ', body[start..end].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return (start > 0 ? "…" : "") + text + (end < body.Length ? "…" : "");
    }
}
