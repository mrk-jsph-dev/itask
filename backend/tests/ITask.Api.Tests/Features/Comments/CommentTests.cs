using System.Net;
using System.Net.Http.Json;
using ITask.Api.Common.Cards;
using ITask.Api.Features.Comments.ListComments;
using ITask.Api.Tests.Infrastructure;

namespace ITask.Api.Tests.Features.Comments;

public sealed class CommentTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<CommentView> PostAsync(HttpClient client, CardOnBoard card, string body)
    {
        var response = await client.PostAsJsonAsync($"{card.Url}/comments", new { body });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CommentView>())!;
    }

    private static Uri Comment(CardOnBoard card, Guid id) => new($"{card.Url}/comments/{id}", UriKind.Relative);

    [Fact]
    public async Task Viewer_can_comment_and_the_tile_counts_comments()
    {
        var card = await CardOnBoard.CreateAsync(factory);

        var comment = await PostAsync(card.Board.Viewer, card, "  Looks good  ");

        Assert.Equal(("Looks good", "Test User"), (comment.Body, comment.AuthorName));
        Assert.Equal(1, (await card.SummaryAsync()).CommentCount);
    }

    [Fact]
    public async Task Mentions_name_board_members_only()
    {
        var card = await CardOnBoard.CreateAsync(factory);
        (await card.Board.Editor.PutAsJsonAsync("/api/me", new { displayName = "Ada Lovelace", avatarColor = "#1D5BF5", timeZone = "UTC" }))
            .EnsureSuccessStatusCode();
        (await card.Board.Stranger.PutAsJsonAsync("/api/me", new { displayName = "Grace", avatarColor = "#1D5BF5", timeZone = "UTC" }))
            .EnsureSuccessStatusCode();

        var comment = await PostAsync(card.Board.Owner, card, "@ada lovelace please check, cc @Grace");

        Assert.Equal(["Ada Lovelace"], comment.Mentions.Select(m => m.DisplayName));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Blank_and_oversize_bodies_are_rejected(string body)
    {
        var card = await CardOnBoard.CreateAsync(factory);

        var blank = await card.Board.Owner.PostAsJsonAsync($"{card.Url}/comments", new { body });
        var tooLong = await card.Board.Owner.PostAsJsonAsync($"{card.Url}/comments", new { body = new string('x', 5001) });

        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    [Fact]
    public async Task Only_the_author_edits()
    {
        var card = await CardOnBoard.CreateAsync(factory);
        var comment = await PostAsync(card.Board.Editor, card, "Frist");

        var byOwner = await card.Board.Owner.PutAsJsonAsync(Comment(card, comment.Id), new { body = "Hijack" });
        var byAuthor = await card.Board.Editor.PutAsJsonAsync(Comment(card, comment.Id), new { body = "First" });
        var edited = (await byAuthor.Content.ReadFromJsonAsync<CommentView>())!;

        Assert.Equal(HttpStatusCode.Forbidden, byOwner.StatusCode);
        Assert.Equal("First", edited.Body);
        Assert.NotNull(edited.EditedAt);
    }

    [Fact]
    public async Task Author_or_owner_deletes_but_not_another_editor()
    {
        var card = await CardOnBoard.CreateAsync(factory);
        var viewers = await PostAsync(card.Board.Viewer, card, "Viewer note");
        var editors = await PostAsync(card.Board.Editor, card, "Editor note");

        var byEditor = await card.Board.Editor.DeleteAsync(Comment(card, viewers.Id));
        var byOwner = await card.Board.Owner.DeleteAsync(Comment(card, viewers.Id));
        var byAuthor = await card.Board.Editor.DeleteAsync(Comment(card, editors.Id));

        Assert.Equal(HttpStatusCode.Forbidden, byEditor.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, byOwner.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, byAuthor.StatusCode);
        Assert.Equal(0, (await card.SummaryAsync()).CommentCount);
    }

    [Fact]
    public async Task List_pages_newest_first()
    {
        var card = await CardOnBoard.CreateAsync(factory);
        foreach (var i in Enumerable.Range(1, 5))
        {
            await PostAsync(card.Board.Owner, card, $"Comment {i}");
        }

        var first = await card.Board.Viewer.GetFromJsonAsync<ListComments.Response>($"{card.Url}/comments?limit=3");
        var second = await card.Board.Viewer.GetFromJsonAsync<ListComments.Response>(
            $"{card.Url}/comments?limit=3&cursor={first!.NextCursor}");
        var bad = await card.Board.Viewer.GetAsync(new Uri($"{card.Url}/comments?cursor=nope", UriKind.Relative));

        Assert.Equal(["Comment 5", "Comment 4", "Comment 3"], first.Items.Select(c => c.Body));
        Assert.Equal(["Comment 2", "Comment 1"], second!.Items.Select(c => c.Body));
        Assert.Null(second.NextCursor);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task Stranger_gets_not_found_and_archived_board_is_a_conflict()
    {
        var card = await CardOnBoard.CreateAsync(factory);

        var read = await card.Board.Stranger.GetAsync(new Uri($"{card.Url}/comments", UriKind.Relative));
        var post = await card.Board.Stranger.PostAsJsonAsync($"{card.Url}/comments", new { body = "Hi" });
        await card.ArchiveBoardAsync();
        var archived = await card.Board.Viewer.PostAsJsonAsync($"{card.Url}/comments", new { body = "Hi" });

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, archived.StatusCode);
    }
}
