using System.Net;
using System.Net.Http.Json;
using ITask.Api.Common.Notifications;
using ITask.Api.Tests.Infrastructure;

namespace ITask.Api.Tests.Features.Notifications;

public sealed class NotificationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Mentions_notify_the_member_and_comments_notify_the_cards_creator()
    {
        var card = await CardOnBoard.CreateAsync(factory);
        await card.Board.Editor.RenameAsync("Ada Lovelace");

        await card.Board.Viewer.CommentAsync(card, "@Ada Lovelace can you check?");

        var ada = Assert.Single(await card.Board.Editor.NotificationsAsync());
        Assert.Equal((NotificationTypes.Mentioned, card.CardId, card.BoardId), (ada.Type, ada.CardId, ada.Board.Id));
        Assert.Equal("Write spec", ada.Data.GetProperty("card").GetProperty("title").GetString());
        Assert.Equal("Test User", ada.Actor!.DisplayName);
        Assert.Null(ada.ReadAt);

        var owner = Assert.Single(await card.Board.Owner.NotificationsAsync());
        Assert.Equal(NotificationTypes.CommentOnMyCard, owner.Type);
        Assert.Empty(await card.Board.Viewer.NotificationsAsync());
    }

    [Fact]
    public async Task A_mentioned_creator_gets_one_notification_and_nobody_hears_about_their_own_action()
    {
        var card = await CardOnBoard.CreateAsync(factory);
        await card.Board.Owner.RenameAsync("Olive Owner");

        await card.Board.Editor.CommentAsync(card, "@Olive Owner ping");
        await card.Board.Owner.CommentAsync(card, "Thanks @Olive Owner, note to self");

        var only = Assert.Single(await card.Board.Owner.NotificationsAsync());
        Assert.Equal(NotificationTypes.Mentioned, only.Type);
    }

    [Fact]
    public async Task Editing_a_comment_notifies_only_newly_mentioned_members()
    {
        var card = await CardOnBoard.CreateAsync(factory);
        await card.Board.Editor.RenameAsync("Ada");
        await card.Board.Viewer.RenameAsync("Grace");
        var response = await card.Board.Owner.PostAsJsonAsync($"{card.Url}/comments", new { body = "@Ada first" });
        var commentId = (await response.Content.ReadFromJsonAsync<ITask.Api.Common.Cards.CommentView>())!.Id;

        (await card.Board.Owner.PutAsJsonAsync($"{card.Url}/comments/{commentId}", new { body = "@Ada and @Grace" }))
            .EnsureSuccessStatusCode();

        Assert.Single(await card.Board.Editor.NotificationsAsync());
        Assert.Equal(NotificationTypes.Mentioned, Assert.Single(await card.Board.Viewer.NotificationsAsync()).Type);
    }

    [Fact]
    public async Task New_assignees_are_notified_but_not_the_one_assigning()
    {
        var card = await CardOnBoard.CreateAsync(factory);
        var editorId = await card.Board.Editor.GetUserIdAsync();
        var ownerId = await card.Board.Owner.GetUserIdAsync();

        await card.Board.Owner.AssignAsync(card, editorId, ownerId);
        await card.Board.Owner.AssignAsync(card, editorId);

        Assert.Equal(NotificationTypes.Assigned, Assert.Single(await card.Board.Editor.NotificationsAsync()).Type);
        Assert.Empty(await card.Board.Owner.NotificationsAsync());
    }

    [Fact]
    public async Task Read_state_unread_filter_and_count()
    {
        var card = await CardOnBoard.CreateAsync(factory);
        await card.Board.Editor.CommentAsync(card, "one");
        await card.Board.Editor.CommentAsync(card, "two");
        await card.Board.Editor.CommentAsync(card, "three");
        var owner = card.Board.Owner;
        Assert.Equal(3, await owner.UnreadCountAsync());

        var newest = (await owner.NotificationsAsync())[0];
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostEmptyAsync($"/api/notifications/{newest.Id}/read")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostEmptyAsync($"/api/notifications/{newest.Id}/read")).StatusCode);

        Assert.Equal(2, await owner.UnreadCountAsync());
        Assert.DoesNotContain(await owner.NotificationsAsync(unread: true), n => n.Id == newest.Id);
        Assert.NotNull((await owner.NotificationsAsync()).Single(n => n.Id == newest.Id).ReadAt);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostEmptyAsync("/api/notifications/read-all")).StatusCode);
        Assert.Equal(0, await owner.UnreadCountAsync());
        Assert.Equal(3, (await owner.NotificationsAsync()).Count);
    }

    [Fact]
    public async Task Pages_newest_first_with_a_cursor()
    {
        var card = await CardOnBoard.CreateAsync(factory);
        for (var i = 0; i < 3; i++)
        {
            await card.Board.Editor.CommentAsync(card, $"comment {i}");
        }

        var first = (await card.Board.Owner.GetFromJsonAsync<NotificationPage>("/api/notifications?limit=2"))!;
        var second = (await card.Board.Owner.GetFromJsonAsync<NotificationPage>(
            $"/api/notifications?limit=2&cursor={first.NextCursor}"))!;

        Assert.Equal(2, first.Items.Count);
        Assert.NotNull(first.NextCursor);
        Assert.Single(second.Items);
        Assert.Null(second.NextCursor);
        Assert.True(first.Items[1].CreatedAt >= second.Items[0].CreatedAt);
    }

    [Fact]
    public async Task Someone_elses_notification_is_not_found()
    {
        var card = await CardOnBoard.CreateAsync(factory);
        await card.Board.Editor.CommentAsync(card, "hello");
        var id = (await card.Board.Owner.NotificationsAsync())[0].Id;

        Assert.Equal(HttpStatusCode.NotFound, (await card.Board.Stranger.PostEmptyAsync($"/api/notifications/{id}/read")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await card.Board.Owner.PostEmptyAsync($"/api/notifications/{Guid.NewGuid()}/read")).StatusCode);
        Assert.Equal(1, await card.Board.Owner.UnreadCountAsync());
    }

    [Fact]
    public async Task Bad_cursor_is_400_and_anonymous_is_401()
    {
        var client = await factory.CreateSignedInClientAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(new Uri("/api/notifications?cursor=nope", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await factory.CreateHttpsClient().GetAsync(new Uri("/api/notifications/unread-count", UriKind.Relative))).StatusCode);
    }
}
