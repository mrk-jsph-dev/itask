using System.Net;
using System.Net.Http.Json;
using ITask.Api.Common.Security;
using ITask.Api.Domain;
using ITask.Api.Features.Ai.BoardDigest;
using ITask.Api.Features.Ai.BoardFromGoal;
using ITask.Api.Features.Ai.BreakDown;
using ITask.Api.Features.Ai.GetAiStatus;
using ITask.Api.Features.Ai.QuickAdd;
using ITask.Api.Features.Ai.Summarize;
using ITask.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Tests.Features.Ai;

public sealed class AiTests(AiApiFactory factory) : IClassFixture<AiApiFactory>
{
    private const string QuickAddAnswer = """
        { "title": "Fix login bug", "dueDate": "2030-01-04", "priority": "High", "labels": ["bug", "nope"], "list": "review" }
        """;

    private static string QuickAddUrl(Guid boardId) => $"/api/boards/{boardId}/ai/quick-add";

    private void Answer(string text) => factory.Ai.Respond = _ => text;

    [Fact]
    public async Task Status_reports_the_daily_limit_and_remaining_requests()
    {
        var client = await factory.CreateSignedInClientAsync();

        var status = await client.GetFromJsonAsync<GetAiStatus.Response>("/api/ai/status");

        Assert.Equal(new GetAiStatus.Response(true, AiApiFactory.DailyCap, AiApiFactory.DailyCap), status);
    }

    [Fact]
    public async Task Quick_add_returns_a_draft_with_only_the_boards_labels_and_lists()
    {
        var owner = await factory.CreateSignedInClientAsync();
        var boardId = await owner.CreateBoardAsync();
        var label = await owner.PostAsJsonAsync($"/api/boards/{boardId}/labels", new { name = "Bug", color = "#EF4444" });
        label.EnsureSuccessStatusCode();
        var labelId = (await label.Content.ReadFromJsonAsync<LabelView>())!.Id;
        var review = (await owner.GetListsAsync(boardId)).Single(l => l.Name == "Review").Id;
        Answer(QuickAddAnswer);

        var response = await owner.PostAsJsonAsync(QuickAddUrl(boardId), new QuickAdd.Request("Fix login bug by Friday, high"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var draft = await response.Content.ReadFromJsonAsync<QuickAdd.Response>();
        Assert.Equal("Fix login bug", draft!.Title);
        Assert.Equal(new DateOnly(2030, 1, 4), draft.DueDate);
        Assert.Equal(CardPriority.High, draft.Priority);
        Assert.Equal([labelId], draft.LabelIds);
        Assert.Equal(review, draft.ListId);
        Assert.Equal(AiApiFactory.DailyCap - 1, draft.Remaining);
        Assert.Contains("Board labels: Bug", factory.Ai.LastPrompt, StringComparison.Ordinal);
        Assert.Contains("Fix login bug by Friday, high", factory.Ai.LastPrompt, StringComparison.Ordinal);
        Assert.NotNull(factory.Ai.Requests.Last().JsonSchema);
    }

    [Fact]
    public async Task Quick_add_validates_the_text()
    {
        var owner = await factory.CreateSignedInClientAsync();
        var boardId = await owner.CreateBoardAsync();

        var response = await owner.PostAsJsonAsync(QuickAddUrl(boardId), new QuickAdd.Request(""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("Stranger", HttpStatusCode.NotFound)]
    [InlineData("Viewer", HttpStatusCode.Forbidden)]
    [InlineData("Editor", HttpStatusCode.OK)]
    public async Task Board_ai_needs_editor_role(string role, HttpStatusCode expected)
    {
        var card = await CardOnBoard.CreateAsync(factory);
        var client = card.Board.For(role);
        Answer("""{ "items": ["Draft outline", "Review"] }""");

        var breakdown = await client.PostAsync(new Uri($"{card.Url}/ai/breakdown", UriKind.Relative), null);
        Answer("A short summary.");
        var summary = await client.PostAsync(new Uri($"{card.Url}/ai/summary", UriKind.Relative), null);
        var digest = await client.PostAsync(new Uri($"/api/boards/{card.BoardId}/ai/digest", UriKind.Relative), null);

        Assert.Equal(expected, breakdown.StatusCode);
        Assert.Equal(expected, summary.StatusCode);
        Assert.Equal(expected, digest.StatusCode);
    }

    [Fact]
    public async Task Breakdown_drops_items_the_card_already_has()
    {
        var card = await CardOnBoard.CreateAsync(factory);
        await card.AddItemAsync(card.Board.Owner, "Review");
        Answer("""{ "items": [" Draft outline ", "review", "Ship it", ""] }""");

        var response = await card.Board.Owner.PostAsync(new Uri($"{card.Url}/ai/breakdown", UriKind.Relative), null);

        var draft = await response.Content.ReadFromJsonAsync<BreakDown.Response>();
        Assert.Equal(["Draft outline", "Ship it"], draft!.Items);
        Assert.Contains("Title: Write spec", factory.Ai.LastPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Summary_sends_this_card_only_and_names_never_emails()
    {
        var card = await CardOnBoard.CreateAsync(factory);
        var other = await card.Board.Owner.CreateCardAsync(card.BoardId, (await card.Board.Owner.GetListsAsync(card.BoardId))[0].Id, "Secret other card");
        (await card.Board.Owner.PostAsJsonAsync($"{card.Url}/comments", new { body = "Decided to use Markdown." })).EnsureSuccessStatusCode();
        Answer("  The spec is being written; Markdown was chosen.  ");

        var response = await card.Board.Owner.PostAsync(new Uri($"{card.Url}/ai/summary", UriKind.Relative), null);

        var result = await response.Content.ReadFromJsonAsync<Summarize.Response>();
        Assert.Equal("The spec is being written; Markdown was chosen.", result!.Summary);
        var prompt = factory.Ai.LastPrompt;
        Assert.Contains("Decided to use Markdown.", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain(other.Title, prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("@example.com", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Digest_counts_due_stuck_and_done_cards()
    {
        var owner = await factory.CreateSignedInClientAsync();
        var boardId = await owner.CreateBoardAsync();
        var todo = (await owner.GetListsAsync(boardId))[0].Id;
        var overdue = await owner.CreateCardAsync(boardId, todo, "Pay invoice");
        (await owner.PatchAsJsonAsync($"{CardTestKit.Cards(boardId)}/{overdue.Id}", new { dueAt = DateTimeOffset.UtcNow.AddDays(-1) }))
            .EnsureSuccessStatusCode();
        var finished = await owner.CreateCardAsync(boardId, todo, "Send report");
        (await owner.PostEmptyAsync($"{CardTestKit.Cards(boardId)}/{finished.Id}/complete")).EnsureSuccessStatusCode();
        var old = await owner.CreateCardAsync(boardId, todo, "Old idea");
        await factory.WithDbAsync(async db =>
        {
            await db.Cards.Where(c => c.Id == old.Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.CreatedAt, DateTimeOffset.UtcNow.AddDays(-30)));
            await db.Activity.Where(a => a.CardId == old.Id).ExecuteDeleteAsync();
        });
        Answer("Due:\n- Pay invoice\nStuck:\n- Old idea\nDone:\n- Send report");

        var response = await owner.PostAsync(new Uri($"/api/boards/{boardId}/ai/digest", UriKind.Relative), null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var digest = await response.Content.ReadFromJsonAsync<BoardDigest.Response>();
        Assert.Equal((1, 1, 1), (digest!.Due, digest.Stuck, digest.Done));
        Assert.StartsWith("Due:", digest.Digest, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Board_from_goal_drafts_and_the_draft_creates_a_board()
    {
        var client = await factory.CreateSignedInClientAsync();
        Answer("""
            {
              "name": "Portfolio site", "emoji": "🎨",
              "lists": ["Plan", "Build", "Launch", "Plan"],
              "cards": [
                { "list": "Plan", "title": "Pick a domain", "checklist": ["Compare prices"] },
                { "list": "Nowhere", "title": "Dropped", "checklist": [] }
              ]
            }
            """);

        var response = await client.PostAsJsonAsync("/api/ai/board-from-goal", new BoardFromGoal.Request("Launch my portfolio site"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var draft = (await response.Content.ReadFromJsonAsync<BoardFromGoal.Response>())!;
        Assert.Equal(["Plan", "Build", "Launch"], draft.Lists);
        Assert.Equal("Pick a domain", Assert.Single(draft.Cards).Title);

        var created = await client.PostAsJsonAsync("/api/boards/from-content", new
        {
            name = draft.Name,
            emoji = draft.Emoji,
            content = new
            {
                lists = draft.Lists.Select(name => new { name }),
                cards = draft.Cards.Select(c => new { list = c.List, title = c.Title, checklist = c.Checklist }),
            },
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var board = await created.Content.ReadFromJsonAsync<ITask.Api.Features.Templates.CreateBoardFromContent.CreateBoardFromContent.Response>();
        var lists = await client.GetListsAsync(board!.Id);
        Assert.Equal(["Plan", "Build", "Launch"], lists.Select(l => l.Name));
        Assert.Equal("Pick a domain", Assert.Single(lists[0].Cards).Title);
    }

    [Fact]
    public async Task Create_board_from_content_validates_lists()
    {
        var client = await factory.CreateSignedInClientAsync();

        var response = await client.PostAsJsonAsync("/api/boards/from-content", new { name = "Empty", content = new { lists = Array.Empty<object>() } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unusable_answers_and_failures_return_502_and_still_count()
    {
        var owner = await factory.CreateSignedInClientAsync();
        var boardId = await owner.CreateBoardAsync();
        Answer("not json");

        var bad = await owner.PostAsJsonAsync(QuickAddUrl(boardId), new QuickAdd.Request("Something"));
        factory.Ai.Fail = true;
        HttpResponseMessage failed;
        try
        {
            failed = await owner.PostAsJsonAsync(QuickAddUrl(boardId), new QuickAdd.Request("Something"));
        }
        finally
        {
            factory.Ai.Fail = false;
        }

        Assert.Equal(HttpStatusCode.BadGateway, bad.StatusCode);
        Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
        var status = await owner.GetFromJsonAsync<GetAiStatus.Response>("/api/ai/status");
        Assert.Equal(AiApiFactory.DailyCap - 2, status!.Remaining);
    }

    [Fact]
    public async Task Daily_cap_returns_429_without_calling_the_model()
    {
        var owner = await factory.CreateSignedInClientAsync();
        var boardId = await owner.CreateBoardAsync();
        Answer(QuickAddAnswer);
        for (var i = 0; i < AiApiFactory.DailyCap; i++)
        {
            (await owner.PostAsJsonAsync(QuickAddUrl(boardId), new QuickAdd.Request("Task"))).EnsureSuccessStatusCode();
        }

        var calls = factory.Ai.Requests.Count;
        var over = await owner.PostAsJsonAsync(QuickAddUrl(boardId), new QuickAdd.Request("Task"));

        Assert.Equal(HttpStatusCode.TooManyRequests, over.StatusCode);
        Assert.Equal(calls, factory.Ai.Requests.Count);
        var userId = await owner.GetUserIdAsync();
        await factory.WithDbAsync(async db =>
        {
            var usage = await db.AiUsage.SingleAsync(u => u.UserId == userId);
            Assert.Equal(AiApiFactory.DailyCap, usage.Requests);
            Assert.Equal(100 * AiApiFactory.DailyCap, usage.InputTokens);
        });
    }

    [Fact]
    public async Task Concurrent_requests_never_exceed_the_cap()
    {
        var owner = await factory.CreateSignedInClientAsync();
        var boardId = await owner.CreateBoardAsync();
        Answer(QuickAddAnswer);

        var responses = await Task.WhenAll(Enumerable.Range(0, AiApiFactory.DailyCap + 3)
            .Select(_ => owner.PostAsJsonAsync(QuickAddUrl(boardId), new QuickAdd.Request("Task"))));

        Assert.Equal(AiApiFactory.DailyCap, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(3, responses.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests));
    }

    [Fact]
    public async Task Demo_accounts_get_403_and_see_ai_disabled()
    {
        var client = await factory.CreateSignedInClientAsync();
        var boardId = await client.CreateBoardAsync();
        var userId = await client.GetUserIdAsync();
        await factory.WithDbAsync(db => db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsDemo, true)));

        var response = await client.PostAsJsonAsync(QuickAddUrl(boardId), new QuickAdd.Request("Task"));
        var status = await client.GetFromJsonAsync<GetAiStatus.Response>("/api/ai/status");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(status!.Enabled);
    }

    [Fact]
    public async Task Archived_board_returns_409()
    {
        var card = await CardOnBoard.CreateAsync(factory);
        await card.ArchiveBoardAsync();

        var response = await card.Board.Owner.PostAsync(new Uri($"{card.Url}/ai/breakdown", UriKind.Relative), null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task The_app_wide_daily_cap_stops_every_user()
    {
        var owner = await factory.CreateSignedInClientAsync();
        var boardId = await owner.CreateBoardAsync();
        Answer(QuickAddAnswer);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        Task SetTotalAsync(int count) => factory.WithDbAsync(async db =>
        {
            await db.UsageCounters.Where(c => c.Name == DailyCounters.Ai && c.Date == today).ExecuteDeleteAsync();
            db.UsageCounters.Add(new UsageCounter { Name = DailyCounters.Ai, Date = today, Count = count });
            await db.SaveChangesAsync();
        });
        await SetTotalAsync(200);

        try
        {
            var calls = factory.Ai.Requests.Count;
            var response = await owner.PostAsJsonAsync(QuickAddUrl(boardId), new QuickAdd.Request("Task"));
            var status = await owner.GetFromJsonAsync<GetAiStatus.Response>("/api/ai/status");

            Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
            Assert.Equal(calls, factory.Ai.Requests.Count);
            Assert.Equal(0, status!.Remaining);
        }
        finally
        {
            await SetTotalAsync(0);
        }
    }

    private sealed record LabelView(Guid Id);
}
