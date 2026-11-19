using System.Net;
using System.Net.Http.Json;
using ITask.Api.Common.Security;
using ITask.Api.Domain;
using ITask.Api.Infrastructure.Email;
using ITask.Api.Infrastructure.Turnstile;
using ITask.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ITask.Api.Tests.Security;

public sealed class AbuseLimitsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Theory]
    [InlineData("Production", false)]
    [InlineData("Development", true)]
    public async Task Missing_turnstile_secret_fails_closed_only_in_production(string environment, bool expected)
    {
        using var http = new HttpClient();
        var verifier = new TurnstileVerifier(
            http,
            Options.Create(new TurnstileOptions()),
            new HostingEnvironment { EnvironmentName = environment },
            NullLogger<TurnstileVerifier>.Instance);

        Assert.Equal(expected, await verifier.VerifyAsync("token", null, CancellationToken.None));
    }

    [Fact]
    public async Task Emails_are_capped_per_recipient_and_per_day()
    {
        // Other tests in this class send emails too, so the day's limit is set relative to them.
        int sentToday;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            sentToday = await scope.ServiceProvider.GetRequiredService<DailyCounters>().CountAsync(DailyCounters.Email, CancellationToken.None);
        }

        var inner = new CapturingEmailSender();
        var sender = new ThrottledEmailSender(
            inner,
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new EmailOptions { DailyPerRecipient = 2, DailyLimit = sentToday + 3 }),
            NullLogger<ThrottledEmailSender>.Instance);
        var victim = $"victim-{Guid.NewGuid():N}@example.com";
        var other = $"other-{Guid.NewGuid():N}@example.com";

        for (var i = 0; i < 4; i++)
        {
            await sender.SendAsync(EmailTemplates.SignUpCode(victim, "123456"), CancellationToken.None);
        }

        await sender.SendAsync(EmailTemplates.SignUpCode(other, "123456"), CancellationToken.None);
        await sender.SendAsync(EmailTemplates.SignUpCode($"third-{Guid.NewGuid():N}@example.com", "123456"), CancellationToken.None);

        Assert.Equal(2, inner.To(victim).Count);
        Assert.Single(inner.To(other));
        Assert.Equal(3, inner.Sent.Count);
        await factory.WithDbAsync(async db =>
            Assert.DoesNotContain(await db.UsageCounters.Select(c => c.Name).ToListAsync(), n => n.Contains('@', StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Board_owners_are_capped()
    {
        var client = await factory.CreateSignedInClientAsync();
        var userId = await client.GetUserIdAsync();
        await factory.WithDbAsync(async db =>
        {
            for (var i = 0; i < Board.MaxOwnedPerUser; i++)
            {
                var board = new Board { Name = $"B{i}" };
                board.Members.Add(new BoardMember { UserId = userId, Role = BoardRole.Owner });
                db.Boards.Add(board);
            }

            await db.SaveChangesAsync();
        });

        var blank = await client.PostAsJsonAsync("/api/boards", new { name = "One more", emoji = "📋" });
        var template = await client.PostAsJsonAsync("/api/templates/kanban/boards", new { name = "One more" });
        var content = await client.PostAsJsonAsync(
            "/api/boards/from-content", new { name = "One more", content = new { lists = new[] { new { name = "To do" } } } });

        Assert.Equal(HttpStatusCode.Conflict, blank.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, template.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, content.StatusCode);
    }

    [Fact]
    public async Task Cards_per_board_are_capped()
    {
        var client = await factory.CreateSignedInClientAsync();
        var boardId = await client.CreateBoardAsync();
        var listId = (await client.GetListsAsync(boardId))[0].Id;
        var userId = await client.GetUserIdAsync();
        await factory.WithDbAsync(async db =>
        {
            db.Cards.AddRange(Enumerable.Range(0, Card.MaxPerBoard).Select(i => new Card
            {
                BoardId = boardId,
                ListId = listId,
                Number = 10_000 + i,
                Title = $"Card {i}",
                Position = i,
                CreatedById = userId,
            }));
            await db.SaveChangesAsync();
        });

        var response = await client.PostAsJsonAsync($"/api/boards/{boardId}/lists/{listId}/cards", new { title = "Too many" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Uploads_stop_at_the_storage_quota_and_the_daily_url_limit()
    {
        var card = await CardOnBoard.CreateAsync(factory);
        var owner = card.Board.Owner;
        var userId = await owner.GetUserIdAsync();
        var upload = new { fileName = "a.txt", contentType = "text/plain", sizeBytes = 1024 };

        (await owner.PostAsJsonAsync($"{card.Url}/attachments/upload-url", upload)).EnsureSuccessStatusCode();

        await factory.WithDbAsync(async db =>
        {
            db.Attachments.Add(new Attachment
            {
                CardId = card.CardId,
                FileName = "big.zip",
                ContentType = "application/zip",
                SizeBytes = 100L * 1024 * 1024,
                BlobName = $"boards/{card.BoardId:N}/cards/{card.CardId:N}/{Guid.NewGuid():N}",
                UploadedById = userId,
            });
            await db.SaveChangesAsync();
        });
        var full = await owner.PostAsJsonAsync($"{card.Url}/attachments/upload-url", upload);
        Assert.Equal(HttpStatusCode.Conflict, full.StatusCode);

        var editor = card.Board.Editor;
        var editorId = await editor.GetUserIdAsync();
        await factory.WithDbAsync(async db =>
        {
            db.UsageCounters.Add(new UsageCounter
            {
                Name = $"uploads:{editorId:N}",
                Date = DateOnly.FromDateTime(DateTime.UtcNow),
                Count = 50,
            });
            await db.SaveChangesAsync();
        });
        var limited = await editor.PostAsJsonAsync($"{card.Url}/attachments/upload-url", upload);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Fact]
    public async Task Daily_counters_never_pass_their_limit_under_concurrency()
    {
        var name = $"test:{Guid.NewGuid():N}";
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(async _ =>
        {
            await using var scope = factory.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<DailyCounters>().TryIncrementAsync(name, 5, CancellationToken.None);
        }));

        Assert.Equal(5, results.Count(r => r));
    }
}
