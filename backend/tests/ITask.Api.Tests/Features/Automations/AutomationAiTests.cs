using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ITask.Api.Common.Cards;
using ITask.Api.Features.Ai.DraftAutomation;
using ITask.Api.Features.Ai.ReviewAutomations;
using ITask.Api.Infrastructure.Ai;
using ITask.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static ITask.Api.Tests.Infrastructure.AutomationTestKit;

namespace ITask.Api.Tests.Features.Automations;

/// <summary>AI drafting of automations and the opt-in ✨ AI actions.</summary>
public sealed class AutomationAiTests(AiApiFactory factory) : IClassFixture<AiApiFactory>
{
    private sealed record Setup(BoardTestKit.BoardWithMembers Board, List<Guid> Lists, CardSummary Card)
    {
        public Guid BoardId => Board.BoardId;

        public string Base => $"/api/boards/{BoardId}";

        public string CardUrl => $"{CardTestKit.Cards(BoardId)}/{Card.Id}";
    }

    private async Task<Setup> SetupAsync()
    {
        factory.Ai.Fail = false;
        var board = await factory.CreateBoardWithMembersAsync();
        var lists = (await board.Owner.GetListsAsync(board.BoardId)).Select(l => l.Id).ToList();
        var card = await board.Owner.CreateCardAsync(board.BoardId, lists[0], "Launch the site");
        return new Setup(board, lists, card);
    }

    private static string Automation(string kind = "Rule", string? list = null, string trigger = "card.completed", string actionType = "move", string? actionList = "Done") =>
        JsonSerializer.Serialize(new
        {
            name = "Finish flow",
            kind,
            list,
            direction = kind == "Doorway" ? "Enter" : null,
            mode = kind == "Doorway" ? "Block" : null,
            trigger = new { type = trigger, list = (string?)null, label = (string?)null, days = (int?)null },
            conditions = kind == "Doorway"
                ? new object[] { new { type = "assignee.set", labels = Array.Empty<string>(), priority = (string?)null, member = (string?)null } }
                : [],
            actions = kind == "Doorway"
                ? Array.Empty<object>()
                : [new { type = actionType, list = actionList, position = (string?)null, label = (string?)null, who = (string?)null, member = (string?)null, priority = (string?)null, days = (int?)null, items = Array.Empty<string>(), text = (string?)null }],
        });

    private static async Task WaitForAsync(Func<Task<bool>> done)
    {
        for (var i = 0; i < 100; i++)
        {
            if (await done())
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.Fail("Timed out waiting for the AI action.");
    }

    [Fact]
    public async Task Describe_it_drafts_a_rule_with_the_boards_own_ids()
    {
        var s = await SetupAsync();
        factory.Ai.Respond = _ => Automation();

        var response = await s.Board.Editor.PostAsJsonAsync($"{s.Base}/ai/automation", new { text = "When a card is completed, move it to Done" });

        response.EnsureSuccessStatusCode();
        var draft = (await response.Content.ReadFromJsonAsync<DraftAutomation.Response>())!.Automation;
        Assert.Equal("card.completed", draft.Trigger.Type);
        Assert.Equal(s.Lists[3], Assert.Single(draft.Actions).ListId);
        Assert.Contains("Lists: To do, In progress, Review, Done", factory.Ai.LastPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Describe_it_rejects_drafts_that_name_things_the_board_lacks()
    {
        var s = await SetupAsync();
        factory.Ai.Respond = _ => Automation(actionList: "Shipped");

        var response = await s.Board.Owner.PostAsJsonAsync($"{s.Base}/ai/automation", new { text = "Move it to shipped" });

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Board.Viewer.PostAsJsonAsync($"{s.Base}/ai/automation", new { text = "x" })).StatusCode);
    }

    [Fact]
    public async Task Review_suggests_automations_from_counts_without_card_text()
    {
        var s = await SetupAsync();
        (await s.Board.Owner.MoveCardAsync(s.BoardId, s.Card, s.Lists[3])).EnsureSuccessStatusCode();
        factory.Ai.Respond = _ => $$"""
            {"suggestions":[
              {"reason":"Cards entered Done once.","automation":{{Automation()}}},
              {"reason":"Broken.","automation":{{Automation(actionList: "Nowhere")}}}
            ]}
            """;

        var response = await s.Board.Owner.PostAsync(new Uri($"{s.Base}/ai/automation-review", UriKind.Relative), null);

        response.EnsureSuccessStatusCode();
        var suggestion = Assert.Single((await response.Content.ReadFromJsonAsync<ReviewAutomations.Response>())!.Suggestions);
        Assert.Equal("Cards entered Done once.", suggestion.Reason);
        Assert.Contains("cards moved into Done: 1", factory.Ai.LastPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Launch the site", factory.Ai.LastPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_ai_action_runs_after_the_change_and_charges_the_rule_creator()
    {
        var s = await SetupAsync();
        await s.Board.Owner.CreateAutomationAsync(s.BoardId, Rule(new { type = "card.completed" }, [new { type = "ai.breakdown" }], name: "Plan it"));
        factory.Ai.Respond = _ => """{"items":["Draft copy","Pick images"]}""";
        var ownerId = await s.Board.Owner.GetUserIdAsync();

        var response = await s.Board.Editor.PostEmptyAsync($"{s.CardUrl}/complete");

        response.EnsureSuccessStatusCode();
        var notice = Assert.Single(response.Runs());
        Assert.Equal("Asked ✨ AI for a checklist", notice.Summary);
        Assert.False(notice.CanUndo);
        await WaitForAsync(async () =>
            (await s.Board.Owner.GetListsAsync(s.BoardId)).SelectMany(l => l.Cards).Single(c => c.Id == s.Card.Id).ChecklistTotal == 2);
        var charged = 0;
        await factory.WithDbAsync(async db => charged = await db.AiUsage.Where(u => u.UserId == ownerId).SumAsync(u => u.Requests));
        Assert.Equal(1, charged);
        Assert.Contains("checklist.added", await factory.ActivityTypesAsync(s.BoardId));
    }

    [Fact]
    public async Task A_failed_ai_action_is_skipped_and_recorded()
    {
        var s = await SetupAsync();
        await s.Board.Owner.CreateAutomationAsync(s.BoardId, Rule(new { type = "card.completed" }, [new { type = "ai.summary" }]));
        factory.Ai.Fail = true;

        (await s.Board.Owner.PostEmptyAsync($"{s.CardUrl}/complete")).EnsureSuccessStatusCode();

        await WaitForAsync(async () => (await factory.ActivityTypesAsync(s.BoardId)).Contains("automation.ai_skipped"));
        factory.Ai.Fail = false;
    }

    [Fact]
    public async Task Ai_actions_only_go_on_instant_rules_one_at_a_time()
    {
        var s = await SetupAsync();

        var hourly = await s.Board.Owner.PostAutomationAsync(s.BoardId, Rule(new { type = "due.overdue" }, [new { type = "ai.triage" }]));
        var two = await s.Board.Owner.PostAutomationAsync(s.BoardId, Rule(new { type = "card.completed" }, [new { type = "ai.triage" }, new { type = "ai.summary" }]));

        Assert.Equal(HttpStatusCode.BadRequest, hourly.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, two.StatusCode);
    }
}

/// <summary>AI actions can't be added where AI is off.</summary>
public sealed class AutomationAiOffTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Ai_actions_are_rejected_when_ai_is_off()
    {
        var owner = await factory.CreateSignedInClientAsync();
        var boardId = await owner.CreateBoardAsync();

        var response = await owner.PostAutomationAsync(boardId, Rule(new { type = "card.completed" }, [new { type = "ai.breakdown" }]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
