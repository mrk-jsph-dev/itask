using System.Net.Http.Json;
using ITask.Api.Common.Persistence;
using ITask.Api.Domain;
using ITask.Api.Features.Boards.CreateBoard;
using ITask.Api.Features.Invites.CreateInvite;
using Microsoft.Extensions.DependencyInjection;

namespace ITask.Api.Tests.Infrastructure;

/// <summary>Builds boards with members in each role through the public API.</summary>
public static class BoardTestKit
{
    public sealed record BoardWithMembers(
        Guid BoardId,
        HttpClient Owner,
        HttpClient Editor,
        HttpClient Viewer,
        HttpClient Stranger)
    {
        public HttpClient For(string role) => role switch
        {
            "Owner" => Owner,
            "Editor" => Editor,
            "Viewer" => Viewer,
            _ => Stranger,
        };
    }

    public static async Task<Guid> CreateBoardAsync(this HttpClient owner, string name = "Team board")
    {
        var response = await owner.PostAsJsonAsync("/api/boards", new CreateBoard.Request(name, "📋"));
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<CreateBoard.Response>();
        return created!.Id;
    }

    public static async Task<CreateInvite.Response> InviteAsync(
        this HttpClient owner, Guid boardId, BoardRole role, string? email = null)
    {
        var response = await owner.PostAsJsonAsync($"/api/boards/{boardId}/invites", new CreateInvite.Request(email, role));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreateInvite.Response>())!;
    }

    public static async Task<HttpClient> JoinAsAsync(this ApiFactory factory, HttpClient owner, Guid boardId, BoardRole role)
    {
        var invite = await owner.InviteAsync(boardId, role);
        var member = await factory.CreateSignedInClientAsync();
        (await member.PostAsync(new Uri($"/api/invites/{invite.Token}/accept", UriKind.Relative), null))
            .EnsureSuccessStatusCode();
        return member;
    }

    public static async Task<BoardWithMembers> CreateBoardWithMembersAsync(this ApiFactory factory)
    {
        var owner = await factory.CreateSignedInClientAsync();
        var boardId = await owner.CreateBoardAsync();
        var editor = await factory.JoinAsAsync(owner, boardId, BoardRole.Editor);
        var viewer = await factory.JoinAsAsync(owner, boardId, BoardRole.Viewer);
        var stranger = await factory.CreateSignedInClientAsync();
        return new BoardWithMembers(boardId, owner, editor, viewer, stranger);
    }

    public static async Task<Guid> GetUserIdAsync(this HttpClient client)
    {
        var me = await client.GetFromJsonAsync<ITask.Api.Features.Profile.GetMe.GetMe.Response>("/api/me");
        return me!.Id;
    }

    /// <summary>Runs <paramref name="action"/> against the factory's database.</summary>
    public static async Task WithDbAsync(this ApiFactory factory, Func<AppDbContext, Task> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await action(db);
    }
}
