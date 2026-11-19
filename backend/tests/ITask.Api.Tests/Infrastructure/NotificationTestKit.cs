using System.Net.Http.Json;
using ITask.Api.Common.Notifications;

namespace ITask.Api.Tests.Infrastructure;

/// <summary>Notification helpers that go through the public API.</summary>
public static class NotificationTestKit
{
    public static async Task<IReadOnlyList<NotificationView>> NotificationsAsync(this HttpClient client, bool unread = false) =>
        (await client.GetFromJsonAsync<NotificationPage>($"/api/notifications{(unread ? "?unread=true" : "")}"))!.Items;

    public static async Task<int> UnreadCountAsync(this HttpClient client) =>
        (await client.GetFromJsonAsync<ITask.Api.Features.Notifications.GetUnreadCount.GetUnreadCount.Response>(
            "/api/notifications/unread-count"))!.Count;

    public static async Task RenameAsync(this HttpClient client, string displayName, string timeZone = "UTC") =>
        (await client.PutAsJsonAsync("/api/me", new { displayName, avatarColor = "#1D5BF5", timeZone }))
            .EnsureSuccessStatusCode();

    public static async Task CommentAsync(this HttpClient client, CardOnBoard card, string body) =>
        (await client.PostAsJsonAsync($"{card.Url}/comments", new { body })).EnsureSuccessStatusCode();

    public static async Task AssignAsync(this HttpClient client, CardOnBoard card, params Guid[] userIds) =>
        (await client.PutAsJsonAsync($"{card.Url}/assignees", new { userIds })).EnsureSuccessStatusCode();
}
