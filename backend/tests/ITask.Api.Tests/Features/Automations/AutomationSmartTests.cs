using System.Net;
using System.Net.Http.Json;
using ITask.Api.Common.Automations;
using ITask.Api.Common.Cards;
using ITask.Api.Features.Automations.AutomationSummary;
using ITask.Api.Features.Automations.DraftFromRecording;
using ITask.Api.Features.Automations.PreviewAutomation;
using ITask.Api.Tests.Infrastructure;
using static ITask.Api.Tests.Infrastructure.AutomationTestKit;

namespace ITask.Api.Tests.Features.Automations;

/// <summary>Show me once, replay preview and the automation receipt.</summary>
public sealed class AutomationSmartTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record Setup(BoardTestKit.BoardWithMembers Board, List<Guid> Lists, CardSummary Card)
    {
        public Guid BoardId => Board.BoardId;

        public HttpClient Owner => Board.Owner;

        public string Base => $"/api/boards/{BoardId}";

        public string CardUrl => $"{CardTestKit.Cards(BoardId)}/{Card.Id}";
    }

    private async Task<Setup> SetupAsync()
    {
        var board = await factory.CreateBoardWithMembersAsync();
        var lists = (await board.Owner.GetListsAsync(board.BoardId)).Select(l => l.Id).ToList();
        var card = await board.Owner.CreateCardAsync(board.BoardId, lists[0], "Write spec");
        return new Setup(board, lists, card);
    }

    private static async Task<Guid> LabelAsync(Setup s, string name)
    {
        var response = await s.Owner.PostAsJsonAsync($"{s.Base}/labels", new { name, color = "#E11D48" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdOnly>())!.Id;
    }

    private sealed record IdOnly(Guid Id);

    [Fact]
    public async Task Show_me_once_turns_recorded_steps_into_a_rule_draft()
    {
        var s = await SetupAsync();
        var bug = await LabelAsync(s, "Bug");
        var since = DateTimeOffset.UtcNow;
        (await s.Owner.MoveCardAsync(s.BoardId, s.Card, s.Lists[2])).EnsureSuccessStatusCode();
        (await s.Owner.PutAsJsonAsync($"{s.CardUrl}/labels", new { labelIds = new[] { bug } })).EnsureSuccessStatusCode();
        var me = await s.Owner.GetUserIdAsync();
        (await s.Owner.PutAsJsonAsync($"{s.CardUrl}/assignees", new { userIds = new[] { me } })).EnsureSuccessStatusCode();

        var response = await s.Owner.PostAsJsonAsync($"{s.Base}/automations/from-recording", new { cardId = s.Card.Id, since });

        response.EnsureSuccessStatusCode();
        var draft = (await response.Content.ReadFromJsonAsync<DraftFromRecording.Response>())!;
        Assert.Equal("Review routine", draft.Name);
        Assert.Equal(new TriggerSpec("card.entered", s.Lists[2]), draft.Trigger);
        Assert.Equal(["label.add", "assign"], draft.Actions.Select(a => a.Type));
        Assert.Equal(bug, draft.Actions[0].LabelId);
        Assert.Equal("actor", draft.Actions[1].Who);
    }

    [Fact]
    public async Task Show_me_once_needs_a_trigger_and_a_step_after_it()
    {
        var s = await SetupAsync();
        var bug = await LabelAsync(s, "Bug");
        var since = DateTimeOffset.UtcNow;
        (await s.Owner.PutAsJsonAsync($"{s.CardUrl}/labels", new { labelIds = new[] { bug } })).EnsureSuccessStatusCode();

        var noTrigger = await s.Owner.PostAsJsonAsync($"{s.Base}/automations/from-recording", new { cardId = s.Card.Id, since });
        var viewer = await s.Board.Viewer.PostAsJsonAsync($"{s.Base}/automations/from-recording", new { cardId = s.Card.Id, since });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, noTrigger.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, viewer.StatusCode);
    }

    [Fact]
    public async Task Preview_replays_instant_triggers_over_the_last_30_days()
    {
        var s = await SetupAsync();
        var other = await s.Owner.CreateCardAsync(s.BoardId, s.Lists[0], "Pick fonts");
        (await s.Owner.MoveCardAsync(s.BoardId, s.Card, s.Lists[3])).EnsureSuccessStatusCode();
        (await s.Owner.MoveCardAsync(s.BoardId, other, s.Lists[3])).EnsureSuccessStatusCode();

        var response = await s.Owner.PostAsJsonAsync($"{s.Base}/automations/preview",
            Rule(new { type = "card.entered", listId = s.Lists[3] }, [new { type = "complete" }]));

        response.EnsureSuccessStatusCode();
        var preview = (await response.Content.ReadFromJsonAsync<PreviewAutomation.Response>())!;
        Assert.Equal(2, preview.Count);
        Assert.Equal("history", preview.Basis);
        Assert.Equal(2, preview.Samples.Count);
    }

    [Fact]
    public async Task Preview_of_a_doorway_counts_cards_that_would_have_been_stopped()
    {
        var s = await SetupAsync();
        (await s.Owner.MoveCardAsync(s.BoardId, s.Card, s.Lists[2])).EnsureSuccessStatusCode();

        var response = await s.Owner.PostAsJsonAsync($"{s.Base}/automations/preview",
            Doorway(s.Lists[2], "Enter", "Block", [new { type = "assignee.set" }]));

        Assert.Equal(1, (await response.Content.ReadFromJsonAsync<PreviewAutomation.Response>())!.Count);
    }

    [Fact]
    public async Task Preview_of_a_time_based_rule_counts_cards_it_would_act_on_now()
    {
        var s = await SetupAsync();
        (await s.Owner.PatchAsJsonAsync(s.CardUrl, new { dueAt = DateTimeOffset.UtcNow.AddHours(-2) })).EnsureSuccessStatusCode();

        var response = await s.Board.Editor.PostAsJsonAsync($"{s.Base}/automations/preview",
            Rule(new { type = "due.overdue" }, [new { type = "priority", priority = "Urgent" }]));

        var preview = (await response.Content.ReadFromJsonAsync<PreviewAutomation.Response>())!;
        Assert.Equal(1, preview.Count);
        Assert.Equal("now", preview.Basis);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Board.Viewer.PostAsJsonAsync($"{s.Base}/automations/preview",
            Rule(new { type = "due.overdue" }, [new { type = "complete" }]))).StatusCode);
    }

    [Fact]
    public async Task The_summary_counts_runs_and_blocked_moves_but_not_undone_ones()
    {
        var s = await SetupAsync();
        await s.Owner.CreateAutomationAsync(s.BoardId, Doorway(s.Lists[2], "Enter", "Block", [new { type = "assignee.set" }], name: "Review gate"));
        await s.Owner.CreateAutomationAsync(s.BoardId, Rule(new { type = "card.completed" }, [new { type = "priority", priority = "High" }], name: "Finish"));
        Assert.Equal(HttpStatusCode.Conflict, (await s.Owner.MoveCardAsync(s.BoardId, s.Card, s.Lists[2])).StatusCode);
        var run = Assert.Single((await s.Owner.PostEmptyAsync($"{s.CardUrl}/complete")).Runs());
        var other = await s.Owner.CreateCardAsync(s.BoardId, s.Lists[0], "Second");
        var undone = Assert.Single((await s.Owner.PostEmptyAsync($"{CardTestKit.Cards(s.BoardId)}/{other.Id}/complete")).Runs());
        (await s.Owner.PostEmptyAsync($"{s.Base}/automation-runs/{undone.RunId}/undo")).EnsureSuccessStatusCode();

        var summary = (await s.Board.Editor.GetFromJsonAsync<AutomationSummary.Response>($"{s.Base}/automations/summary"))!;

        Assert.Equal(1, summary.Ran);
        Assert.Equal(1, summary.Blocked);
        Assert.Equal("Automations made 1 change; doorways blocked 1 move.", summary.Sentence);
        Assert.Equal(["Finish", "Review gate"], summary.Rules.Select(r => r.Name).Order());
        Assert.NotEqual(Guid.Empty, run.RunId);
    }

    [Fact]
    public void Receipt_sentences_read_naturally()
    {
        Assert.Equal("Automations made 23 changes.", new BoardReceipt(Guid.Empty, "B", 23, 0, []).Sentence());
        Assert.Equal("Doorways blocked 1 move.", new BoardReceipt(Guid.Empty, "B", 0, 1, []).Sentence());
        Assert.Equal("", new BoardReceipt(Guid.Empty, "B", 0, 0, []).Sentence());
    }
}
