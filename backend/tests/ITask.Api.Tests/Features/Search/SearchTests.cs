using System.Net;
using System.Net.Http.Json;
using ITask.Api.Features.Search.GlobalSearch;
using ITask.Api.Tests.Infrastructure;

namespace ITask.Api.Tests.Features.Search;

public sealed class SearchTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    /// <summary>A word no other test in the shared database uses.</summary>
    private static string Unique() => $"zq{Guid.NewGuid():N}"[..14];

    private static async Task<GlobalSearch.Response> SearchAsync(HttpClient client, string q)
    {
        var response = await client.GetAsync(new Uri($"/api/search?q={Uri.EscapeDataString(q)}", UriKind.Relative));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GlobalSearch.Response>())!;
    }

    private static async Task<(Guid BoardId, Guid ListId)> BoardAsync(HttpClient owner, string name = "Team board")
    {
        var boardId = await owner.CreateBoardAsync(name);
        return (boardId, (await owner.GetListsAsync(boardId))[0].Id);
    }

    [Fact]
    public async Task Finds_boards_cards_and_comments_case_insensitively()
    {
        var word = Unique();
        var owner = await factory.CreateSignedInClientAsync();
        var (boardId, listId) = await BoardAsync(owner, $"Launch {word} board");
        var card = await owner.CreateCardAsync(boardId, listId, $"Ship the {word.ToUpperInvariant()} page");
        (await owner.PostAsJsonAsync(
                $"{CardTestKit.Cards(boardId)}/{card.Id}/comments",
                new { body = $"Some context first, then the {word} detail, and more text afterwards." }))
            .EnsureSuccessStatusCode();

        var result = await SearchAsync(owner, word.ToUpperInvariant());

        var board = Assert.Single(result.Boards);
        Assert.Equal(boardId, board.Id);
        var hit = Assert.Single(result.Cards);
        Assert.Equal((card.Id, card.Key, "To do", false), (hit.Id, hit.Key, hit.ListName, hit.KeyMatch));
        var comment = Assert.Single(result.Comments);
        Assert.Equal((card.Id, boardId, card.Key), (comment.CardId, comment.BoardId, comment.CardKey));
        Assert.Contains(word, comment.Snippet, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nothing_comes_back_from_boards_the_user_is_not_in()
    {
        var word = Unique();
        var card = await CardOnBoard.CreateAsync(factory);
        var owner = card.Board.Owner;
        (await owner.PatchAsJsonAsync(card.Url, new { title = $"Secret {word}" })).EnsureSuccessStatusCode();
        (await owner.PostAsJsonAsync($"{card.Url}/comments", new { body = $"Also {word}" })).EnsureSuccessStatusCode();
        var summary = await card.SummaryAsync();

        var asViewer = await SearchAsync(card.Board.Viewer, word);
        var asStranger = await SearchAsync(card.Board.Stranger, word);
        var strangerByKey = await SearchAsync(card.Board.Stranger, summary.Key);

        Assert.Single(asViewer.Cards);
        Assert.Single(asViewer.Comments);
        Assert.Empty(asStranger.Cards);
        Assert.Empty(asStranger.Comments);
        Assert.Empty(strangerByKey.Cards);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Card_key_returns_that_card_first(bool lowerCase)
    {
        var owner = await factory.CreateSignedInClientAsync();
        var (boardId, listId) = await BoardAsync(owner);
        var first = await owner.CreateCardAsync(boardId, listId, "First");
        await owner.CreateCardAsync(boardId, listId, $"Mentions {first.Key} in the title");

        var result = await SearchAsync(owner, lowerCase ? first.Key.ToLowerInvariant() : first.Key);

        Assert.Equal(2, result.Cards.Count);
        Assert.Equal((first.Id, true), (result.Cards[0].Id, result.Cards[0].KeyMatch));
        Assert.False(result.Cards[1].KeyMatch);
    }

    [Fact]
    public async Task Archived_cards_and_boards_are_hidden()
    {
        var word = Unique();
        var owner = await factory.CreateSignedInClientAsync();
        var (boardId, listId) = await BoardAsync(owner, $"Old {word}");
        var card = await owner.CreateCardAsync(boardId, listId, $"Card {word}");
        var other = await BoardAsync(owner);
        await owner.CreateCardAsync(other.BoardId, other.ListId, $"Live {word}");

        (await owner.PostEmptyAsync($"{CardTestKit.Cards(boardId)}/{card.Id}/archive")).EnsureSuccessStatusCode();
        var afterCardArchive = await SearchAsync(owner, word);
        (await owner.PostEmptyAsync($"/api/boards/{boardId}/archive")).EnsureSuccessStatusCode();
        var afterBoardArchive = await SearchAsync(owner, word);

        Assert.Equal([$"Live {word}"], afterCardArchive.Cards.Select(c => c.Title));
        Assert.Single(afterCardArchive.Boards);
        Assert.Empty(afterBoardArchive.Boards);
    }

    [Fact]
    public async Task Renamed_card_is_found_by_its_new_title_only()
    {
        var before = Unique();
        var after = Unique();
        var owner = await factory.CreateSignedInClientAsync();
        var (boardId, listId) = await BoardAsync(owner);
        var card = await owner.CreateCardAsync(boardId, listId, before);

        (await owner.PatchAsJsonAsync($"{CardTestKit.Cards(boardId)}/{card.Id}", new { title = $"Now {after}" }))
            .EnsureSuccessStatusCode();

        Assert.Empty((await SearchAsync(owner, before)).Cards);
        Assert.Single((await SearchAsync(owner, after)).Cards);
    }

    [Fact]
    public async Task Wildcards_in_the_query_match_literally()
    {
        var word = Unique();
        var owner = await factory.CreateSignedInClientAsync();
        var (boardId, listId) = await BoardAsync(owner);
        await owner.CreateCardAsync(boardId, listId, $"{word} 100% done");
        await owner.CreateCardAsync(boardId, listId, $"{word} 100 done");

        var percent = await SearchAsync(owner, "100%");
        var underscore = await SearchAsync(owner, "1_0");

        Assert.Equal([$"{word} 100% done"], percent.Cards.Select(c => c.Title));
        Assert.Empty(underscore.Cards);
    }

    [Theory]
    [InlineData("q=a")]
    [InlineData("q=%20%20a%20")]
    [InlineData("")]
    [InlineData("q=abc&limit=0")]
    [InlineData("q=abc&limit=51")]
    public async Task Bad_queries_are_rejected(string query)
    {
        var client = await factory.CreateSignedInClientAsync();

        var response = await client.GetAsync(new Uri($"/api/search?{query}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Over_long_queries_are_rejected()
    {
        var client = await factory.CreateSignedInClientAsync();

        var response = await client.GetAsync(new Uri($"/api/search?q={new string('a', 101)}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Requires_sign_in()
    {
        var response = await factory.CreateHttpsClient().GetAsync(new Uri("/api/search?q=abc", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void Snippet_trims_around_the_match_and_collapses_whitespace()
    {
        var body = new string('a', 60) + "\n\nneedle  here" + new string('b', 100);

        var snippet = GlobalSearch.Snippet(body, body.IndexOf("needle", StringComparison.Ordinal), 6);

        Assert.StartsWith("…", snippet, StringComparison.Ordinal);
        Assert.EndsWith("…", snippet, StringComparison.Ordinal);
        Assert.Contains("a needle here", snippet, StringComparison.Ordinal);
    }
}

public sealed class SearchRateLimitTests(SearchRateLimitTests.Factory factory) : IClassFixture<SearchRateLimitTests.Factory>
{
    public sealed class Factory : ApiFactory
    {
        protected override Dictionary<string, string?> ExtraConfig => new() { ["RateLimits:SearchPerMinute"] = "2" };
    }

    [Fact]
    public async Task Search_is_rate_limited_per_user()
    {
        var client = await factory.CreateSignedInClientAsync();
        var other = await factory.CreateSignedInClientAsync();
        var url = new Uri("/api/search?q=abc", UriKind.Relative);

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            statuses.Add((await client.GetAsync(url)).StatusCode);
        }

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses);
        Assert.Equal(HttpStatusCode.OK, (await other.GetAsync(url)).StatusCode);
    }
}
