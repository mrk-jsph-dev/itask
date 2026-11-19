using System.Net;
using ITask.Api.Common.Notifications;
using ITask.Api.Domain;
using ITask.Api.Tests.Infrastructure;

namespace ITask.Api.Tests.Features.Notifications;

public sealed class InviteNotificationTests(EmailApiFactory factory) : IClassFixture<EmailApiFactory>
{
    [Fact]
    public async Task An_email_invite_is_emailed_and_shown_to_an_existing_account_who_can_accept_in_app()
    {
        var email = $"invitee-{Guid.NewGuid():N}@example.com";
        var invitee = await factory.CreateSignedInClientAsync(email);
        var owner = await factory.CreateSignedInClientAsync();
        var boardId = await owner.CreateBoardAsync("Launch");

        var invite = await owner.InviteAsync(boardId, BoardRole.Editor, email);

        var mail = Assert.Single(factory.Emails.To(email), m => m.Subject.Contains("invited", StringComparison.Ordinal));
        Assert.Contains("Launch", mail.Subject, StringComparison.Ordinal);
        Assert.Contains($"https://app.example.test/invite/{invite.Token}", mail.Text, StringComparison.Ordinal);

        var notification = Assert.Single(await invitee.NotificationsAsync());
        Assert.Equal((NotificationTypes.Invited, "Launch"), (notification.Type, notification.Board.Name));
        Assert.Equal(invite.Id, notification.Data.GetProperty("inviteId").GetGuid());

        var accepted = await invitee.PostEmptyAsync($"/api/me/invites/{invite.Id}/accept");
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await invitee.GetAsync(new Uri($"/api/boards/{boardId}", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.Gone, (await invitee.PostEmptyAsync($"/api/me/invites/{invite.Id}/accept")).StatusCode);
    }

    [Fact]
    public async Task Only_the_invited_address_can_accept_in_app_and_link_invites_are_not_found()
    {
        var email = $"invitee-{Guid.NewGuid():N}@example.com";
        var owner = await factory.CreateSignedInClientAsync();
        var boardId = await owner.CreateBoardAsync();
        var emailInvite = await owner.InviteAsync(boardId, BoardRole.Viewer, email);
        var linkInvite = await owner.InviteAsync(boardId, BoardRole.Viewer);
        var other = await factory.CreateSignedInClientAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await other.PostEmptyAsync($"/api/me/invites/{emailInvite.Id}/accept")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostEmptyAsync($"/api/me/invites/{linkInvite.Id}/accept")).StatusCode);
        Assert.Single(factory.Emails.To(email), m => m.Subject.Contains("invited", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Link_invites_send_no_email_and_notify_nobody()
    {
        var owner = await factory.CreateSignedInClientAsync();
        var boardId = await owner.CreateBoardAsync();
        var before = factory.Emails.Sent.Count;

        await owner.InviteAsync(boardId, BoardRole.Viewer);

        Assert.Equal(before, factory.Emails.Sent.Count);
    }
}
