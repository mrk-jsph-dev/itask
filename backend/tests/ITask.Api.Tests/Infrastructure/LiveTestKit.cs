using System.Net;
using System.Net.Http.Json;
using System.Threading.Channels;
using ITask.Api.Common.Realtime;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.SignalR.Client;

namespace ITask.Api.Tests.Infrastructure;

/// <summary>A signed-in user whose cookie can also open hub connections.</summary>
public sealed record LiveUser(HttpClient Client, CookieContainer Cookies);

/// <summary>A hub connection that collects what the server pushes to it.</summary>
public sealed class LiveConnection : IAsyncDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    public LiveConnection(HubConnection hub)
    {
        Hub = hub;
        hub.On<BoardEvent>(BoardHub.EventMethod, e => Events.Writer.TryWrite(e));
        hub.On<PresenceUpdate>(BoardHub.PresenceMethod, p => Presence.Writer.TryWrite(p));
        hub.On<AccessLost>(BoardHub.AccessLostMethod, a => AccessLost.Writer.TryWrite(a));
    }

    public HubConnection Hub { get; }

    public Channel<BoardEvent> Events { get; } = Channel.CreateUnbounded<BoardEvent>();

    public Channel<PresenceUpdate> Presence { get; } = Channel.CreateUnbounded<PresenceUpdate>();

    public Channel<AccessLost> AccessLost { get; } = Channel.CreateUnbounded<AccessLost>();

    public string ConnectionId => Hub.ConnectionId!;

    public Task<IReadOnlyList<PresenceUser>> JoinAsync(Guid boardId) =>
        Hub.InvokeAsync<IReadOnlyList<PresenceUser>>("Join", boardId);

    /// <summary>The next pushed event, skipping none; fails after a few seconds.</summary>
    public static async Task<T> NextAsync<T>(Channel<T> channel)
    {
        using var timeout = new CancellationTokenSource(Wait);
        return await channel.Reader.ReadAsync(timeout.Token);
    }

    /// <summary>The next presence update listing exactly <paramref name="count"/> people.</summary>
    public async Task<PresenceUpdate> PresenceOfAsync(int count)
    {
        while (true)
        {
            var update = await NextAsync(Presence);
            if (update.Users.Count == count)
            {
                return update;
            }
        }
    }

    public ValueTask DisposeAsync() => Hub.DisposeAsync();
}

public static class LiveTestKit
{
    public static async Task<LiveUser> CreateLiveUserAsync(this ApiFactory factory)
    {
        var cookies = new CookieContainer();
        var client = factory.CreateDefaultClient(new Uri("https://localhost"), new CookieContainerHandler(cookies));
        var email = $"user-{Guid.NewGuid():N}@example.com";
        (await client.PostAsJsonAsync("/api/auth/register", new { displayName = "Live User", email, password = ApiFactory.Password }))
            .EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/api/auth/login?useCookies=true", new { email, password = ApiFactory.Password }))
            .EnsureSuccessStatusCode();
        return new LiveUser(client, cookies);
    }

    public static async Task<LiveUser> JoinLiveAsync(this ApiFactory factory, HttpClient owner, Guid boardId, BoardRole role)
    {
        var invite = await owner.InviteAsync(boardId, role);
        var member = await factory.CreateLiveUserAsync();
        (await member.Client.PostAsync(new Uri($"/api/invites/{invite.Token}/accept", UriKind.Relative), null))
            .EnsureSuccessStatusCode();
        return member;
    }

    /// <summary>Opens a hub connection as <paramref name="user"/> (anonymous when null), over long polling.</summary>
    public static async Task<LiveConnection> ConnectAsync(this ApiFactory factory, LiveUser? user)
    {
        var url = new Uri($"https://localhost{BoardHub.Path}");
        var hub = new HubConnectionBuilder()
            .WithUrl(url, options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                if (user is not null)
                {
                    // The test server handler replaces the client's own, so the cookie goes in as a header.
                    options.Headers["Cookie"] = user.Cookies.GetCookieHeader(url);
                }
            })
            .Build();
        var connection = new LiveConnection(hub);
        await hub.StartAsync();
        return connection;
    }

    /// <summary>Sends requests on behalf of a hub connection, so the server skips echoing to it.</summary>
    public static void ActAs(this HttpClient client, LiveConnection? connection)
    {
        client.DefaultRequestHeaders.Remove("X-Live-Connection");
        if (connection is not null)
        {
            client.DefaultRequestHeaders.Add("X-Live-Connection", connection.ConnectionId);
        }
    }
}
