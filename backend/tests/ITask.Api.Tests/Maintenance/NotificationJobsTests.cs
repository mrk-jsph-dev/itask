using System.Globalization;
using System.Net.Http.Json;
using ITask.Api.Common.Notifications;
using ITask.Api.Domain;
using ITask.Api.Infrastructure.Notifications;
using ITask.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Tests.Maintenance;

public sealed class NotificationJobsTests(EmailApiFactory factory) : IClassFixture<EmailApiFactory>
{
    private static readonly DateTimeOffset Now = new(2030, 3, 4, 10, 0, 0, TimeSpan.Zero); // a Monday

    private static async Task SetDueAsync(HttpClient client, CardOnBoard card, DateTimeOffset? dueAt) =>
        (await client.PatchAsJsonAsync(card.Url, new { dueAt })).EnsureSuccessStatusCode();

    private Task<List<Notification>> RemindersForAsync(Guid cardId) =>
        WithDb(db => db.Notifications.AsNoTracking().Where(n => n.CardId == cardId && n.Key != null).ToListAsync());

    private async Task<T> WithDb<T>(Func<ITask.Api.Common.Persistence.AppDbContext, Task<T>> query)
    {
        T result = default!;
        await factory.WithDbAsync(async db => result = await query(db));
        return result;
    }

    [Fact]
    public async Task Due_soon_goes_to_assignees_once_and_again_after_the_date_moves()
    {
        var card = await CardOnBoard.CreateAsync(factory);
        var editorId = await card.Board.Editor.GetUserIdAsync();
        await card.Board.Owner.AssignAsync(card, editorId);
        await SetDueAsync(card.Board.Owner, card, Now.AddHours(5));

        await factory.RunJobAsync("due-reminders", Now);
        await factory.RunJobAsync("due-reminders", Now.AddHours(1));
        var first = Assert.Single(await RemindersForAsync(card.CardId));
        Assert.Equal((NotificationTypes.DueSoon, editorId, (Guid?)null), (first.Type, first.UserId, first.ActorId));

        await SetDueAsync(card.Board.Owner, card, Now.AddHours(6));
        await factory.RunJobAsync("due-reminders", Now.AddHours(1));
        Assert.Equal(2, (await RemindersForAsync(card.CardId)).Count);
    }

    [Fact]
    public async Task Unassigned_cards_remind_the_creator_and_overdue_follows_due_soon()
    {
        var card = await CardOnBoard.CreateAsync(factory);
        var ownerId = await card.Board.Owner.GetUserIdAsync();
        await SetDueAsync(card.Board.Owner, card, Now.AddHours(2));

        await factory.RunJobAsync("due-reminders", Now);
        await factory.RunJobAsync("due-reminders", Now.AddHours(3));

        var types = (await RemindersForAsync(card.CardId)).Select(n => (n.Type, n.UserId)).Order().ToList();
        Assert.Equal([(NotificationTypes.DueSoon, ownerId), (NotificationTypes.Overdue, ownerId)], types);
        Assert.Contains(await card.Board.Owner.NotificationsAsync(), n => n.Type == NotificationTypes.Overdue && n.Actor is null);
    }

    [Fact]
    public async Task Done_far_off_and_long_overdue_cards_are_skipped()
    {
        var done = await CardOnBoard.CreateAsync(factory);
        await SetDueAsync(done.Board.Owner, done, Now.AddHours(1));
        await done.Board.Owner.PostEmptyAsync($"{done.Url}/complete");
        var later = await CardOnBoard.CreateAsync(factory);
        await SetDueAsync(later.Board.Owner, later, Now.AddDays(3));
        var old = await CardOnBoard.CreateAsync(factory);
        await SetDueAsync(old.Board.Owner, old, Now.AddDays(-10));

        await factory.RunJobAsync("due-reminders", Now);

        Assert.Empty(await RemindersForAsync(done.CardId));
        Assert.Empty(await RemindersForAsync(later.CardId));
        Assert.Empty(await RemindersForAsync(old.CardId));
    }

    private async Task<(HttpClient Client, string Email)> SubscriberAsync(string digest, string timeZone = "UTC", bool confirmed = true)
    {
        var email = $"digest-{Guid.NewGuid():N}@example.com";
        var client = await factory.CreateSignedInClientAsync(email);
        await client.RenameAsync("Dana", timeZone);
        (await client.PutAsJsonAsync("/api/me/notification-preferences", new
        {
            digest,
            types = new DigestTypes(true, true, true, true, true, CommentOnMyCard: false),
        })).EnsureSuccessStatusCode();

        var userId = await client.GetUserIdAsync();
        await factory.WithDbAsync(async db =>
        {
            await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.EmailConfirmed, confirmed));
            await db.NotificationPreferences.Where(p => p.UserId == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.LastDigestAt, DateTimeOffset.UtcNow.AddDays(-2)));
        });
        return (client, email);
    }

    private async Task MentionAsync(HttpClient subscriber, string name)
    {
        var owner = await factory.CreateSignedInClientAsync();
        var boardId = await owner.CreateBoardAsync("Roadmap");
        var invite = await owner.InviteAsync(boardId, BoardRole.Editor);
        (await subscriber.PostEmptyAsync($"/api/invites/{invite.Token}/accept")).EnsureSuccessStatusCode();
        var list = (await owner.GetListsAsync(boardId))[0].Id;
        var card = await owner.CreateCardAsync(boardId, list, "Ship it");
        (await owner.PostAsJsonAsync($"{CardTestKit.Cards(boardId)}/{card.Id}/comments", new { body = $"@{name} look" }))
            .EnsureSuccessStatusCode();
        (await owner.PostAsJsonAsync($"{CardTestKit.Cards(boardId)}/{card.Id}/comments", new { body = "plain comment" }))
            .EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Daily_digest_goes_out_after_8am_local_once_and_skips_excluded_types()
    {
        var (client, email) = await SubscriberAsync("Daily", "Asia/Manila");
        await MentionAsync(client, "Dana");
        var mentionTime = (await client.NotificationsAsync())[0].CreatedAt;
        var beforeEight = new DateTimeOffset(mentionTime.UtcDateTime.Date.AddDays(1).AddHours(-1), TimeSpan.Zero); // 07:00 Manila
        var afterEight = beforeEight.AddHours(2);
        var manila = TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila");
        var userId = await client.GetUserIdAsync();
        await factory.WithDbAsync(db => db.NotificationPreferences.Where(p => p.UserId == userId).ExecuteUpdateAsync(s =>
            s.SetProperty(p => p.LastDigestAt, NotificationDigestJob.LastDue(beforeEight, manila, DigestFrequency.Daily))));

        await factory.RunJobAsync("notification-digest", beforeEight);
        Assert.Empty(factory.Emails.Digests(email));

        await factory.RunJobAsync("notification-digest", afterEight);
        await factory.RunJobAsync("notification-digest", afterEight.AddHours(3));

        var digest = Assert.Single(factory.Emails.Digests(email));
        Assert.Contains("mentioned you on", digest.Text, StringComparison.Ordinal);
        Assert.Contains("Ship it", digest.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("commented on", digest.Text, StringComparison.Ordinal);
        Assert.Contains("https://app.example.test/boards/", digest.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unconfirmed_off_and_already_read_send_nothing()
    {
        var (unconfirmed, unconfirmedEmail) = await SubscriberAsync("Daily", confirmed: false);
        await MentionAsync(unconfirmed, "Dana");
        var (reader, readerEmail) = await SubscriberAsync("Daily");
        await MentionAsync(reader, "Dana");
        await reader.PostEmptyAsync("/api/notifications/read-all");

        await factory.RunJobAsync("notification-digest", DateTimeOffset.UtcNow.AddDays(1));

        Assert.Empty(factory.Emails.Digests(unconfirmedEmail));
        Assert.Empty(factory.Emails.Digests(readerEmail));
    }

    [Theory]
    [InlineData("2030-03-04T07:59:00Z", "Daily", "2030-03-03T08:00:00Z")]
    [InlineData("2030-03-04T08:00:00Z", "Daily", "2030-03-04T08:00:00Z")]
    [InlineData("2030-03-06T12:00:00Z", "Weekly", "2030-03-04T08:00:00Z")]
    [InlineData("2030-03-04T07:00:00Z", "Weekly", "2030-02-25T08:00:00Z")]
    public void Last_due_is_the_latest_local_8am(string now, string frequency, string expected) =>
        Assert.Equal(
            DateTimeOffset.Parse(expected, CultureInfo.InvariantCulture),
            NotificationDigestJob.LastDue(
                DateTimeOffset.Parse(now, CultureInfo.InvariantCulture), TimeZoneInfo.Utc, Enum.Parse<DigestFrequency>(frequency)));

    [Fact]
    public void Last_due_follows_the_users_time_zone()
    {
        var manila = TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila");
        Assert.Equal(
            new DateTimeOffset(2030, 3, 4, 0, 0, 0, TimeSpan.Zero),
            NotificationDigestJob.LastDue(new DateTimeOffset(2030, 3, 4, 1, 0, 0, TimeSpan.Zero), manila, DigestFrequency.Daily));
    }
}
