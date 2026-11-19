using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ITask.Api.Common.Automations;
using ITask.Api.Common.Cards;
using ITask.Api.Domain;
using ITask.Api.Tests.Infrastructure;
using ITask.Api.Tests.Maintenance;
using Microsoft.EntityFrameworkCore;
using static ITask.Api.Tests.Infrastructure.AutomationTestKit;

namespace ITask.Api.Tests.Features.Automations;

public sealed class AutomationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    /// <summary>A board with members, its four default lists and one card in To do.</summary>
    private sealed record Setup(BoardTestKit.BoardWithMembers Board, Guid Todo, Guid Doing, Guid Review, Guid Done, CardSummary Card)
    {
        public Guid BoardId => Board.BoardId;

        public HttpClient Owner => Board.Owner;

        public string CardUrl => $"{CardTestKit.Cards(BoardId)}/{Card.Id}";

        public async Task<CardSummary> CardAsync(Guid? id = null) =>
            (await Owner.GetListsAsync(BoardId)).SelectMany(l => l.Cards).Single(c => c.Id == (id ?? Card.Id));

        public async Task<Guid> LabelAsync(string name)
        {
            var response = await Owner.PostAsJsonAsync($"/api/boards/{BoardId}/labels", new { name, color = "#E11D48" });
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        }
    }

    private async Task<Setup> SetupAsync()
    {
        var board = await factory.CreateBoardWithMembersAsync();
        var lists = (await board.Owner.GetListsAsync(board.BoardId)).Select(l => l.Id).ToList();
        var card = await board.Owner.CreateCardAsync(board.BoardId, lists[0], "Write spec");
        return new Setup(board, lists[0], lists[1], lists[2], lists[3], card);
    }

    private static object Trigger(string type, Guid? listId = null, Guid? labelId = null, int? days = null) =>
        new { type, listId, labelId, days };

    [Fact]
    public async Task Owner_creates_a_rule_and_everyone_with_edit_rights_sees_it()
    {
        var s = await SetupAsync();

        var created = await s.Owner.CreateAutomationAsync(s.BoardId,
            Rule(Trigger("card.completed"), [new { type = "move", listId = s.Done }], name: "Finish flow"));

        Assert.Equal("Finish flow", created.Name);
        Assert.Equal("instant", created.Timing);
        Assert.Null(created.Problem);
        var listed = await s.Board.Editor.GetFromJsonAsync<List<AutomationView>>(AutomationTestKit.Automations(s.BoardId));
        Assert.Equal(created.Id, Assert.Single(listed!).Id);
    }

    [Fact]
    public async Task Viewers_see_only_doorways_and_strangers_get_404()
    {
        var s = await SetupAsync();
        await s.Owner.CreateAutomationAsync(s.BoardId, Rule(Trigger("card.completed"), [new { type = "move", listId = s.Done }]));
        await s.Owner.CreateAutomationAsync(s.BoardId, Doorway(s.Review, "Enter", "Block", [new { type = "assignee.set" }]));

        var viewer = await s.Board.Viewer.GetFromJsonAsync<List<AutomationView>>(AutomationTestKit.Automations(s.BoardId));
        Assert.Equal(AutomationKind.Doorway, Assert.Single(viewer!).Kind);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Board.Stranger.GetAsync(new Uri(AutomationTestKit.Automations(s.BoardId), UriKind.Relative))).StatusCode);
    }

    [Theory]
    [InlineData("Editor", HttpStatusCode.Forbidden)]
    [InlineData("Viewer", HttpStatusCode.Forbidden)]
    [InlineData("Stranger", HttpStatusCode.NotFound)]
    public async Task Only_owners_add_automations(string role, HttpStatusCode expected)
    {
        var s = await SetupAsync();

        var response = await s.Board.For(role).PostAutomationAsync(s.BoardId, Rule(Trigger("card.completed"), [new { type = "complete" }]));

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Ids_from_another_board_and_unknown_types_are_rejected()
    {
        var s = await SetupAsync();
        var other = await SetupAsync();

        var foreignList = await s.Owner.PostAutomationAsync(s.BoardId, Rule(Trigger("card.completed"), [new { type = "move", listId = other.Done }]));
        var unknown = await s.Owner.PostAutomationAsync(s.BoardId, Rule(Trigger("card.teleported"), [new { type = "complete" }]));
        var noActions = await s.Owner.PostAutomationAsync(s.BoardId, Rule(Trigger("card.completed"), []));
        var unfixable = await s.Owner.PostAutomationAsync(s.BoardId, Doorway(s.Review, "Enter", "Fix", [new { type = "due.set" }]));

        Assert.Equal(HttpStatusCode.BadRequest, foreignList.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noActions.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unfixable.StatusCode);
    }

    [Fact]
    public async Task A_board_holds_at_most_25_automations()
    {
        var s = await SetupAsync();
        for (var i = 0; i < AutomationRule.MaxPerBoard; i++)
        {
            await s.Owner.CreateAutomationAsync(s.BoardId, Rule(Trigger("card.completed"), [new { type = "complete" }], name: $"Rule {i}"));
        }

        var response = await s.Owner.PostAutomationAsync(s.BoardId, Rule(Trigger("card.completed"), [new { type = "complete" }]));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Update_checks_the_row_version_and_toggle_and_delete_work()
    {
        var s = await SetupAsync();
        var rule = await s.Owner.CreateAutomationAsync(s.BoardId, Rule(Trigger("card.completed"), [new { type = "complete" }]));
        var url = $"{AutomationTestKit.Automations(s.BoardId)}/{rule.Id}";
        var body = new { name = "Renamed", trigger = new { type = "card.reopened" }, conditions = Array.Empty<object>(), actions = new[] { new { type = "archive" } } };

        var updated = await s.Owner.PutAsJsonAsync(url, new { body.name, body.trigger, body.conditions, body.actions, rule.RowVersion });
        var stale = await s.Owner.PutAsJsonAsync(url, new { body.name, body.trigger, body.conditions, body.actions, rule.RowVersion });
        var off = await s.Owner.PutAsJsonAsync($"{url}/enabled", new { enabled = false });
        var deleted = await s.Owner.DeleteAsync(new Uri(url, UriKind.Relative));

        Assert.Equal("Renamed", (await updated.Content.ReadFromJsonAsync<AutomationView>())!.Name);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.False((await off.Content.ReadFromJsonAsync<AutomationView>())!.Enabled);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Empty((await s.Owner.GetFromJsonAsync<List<AutomationView>>(AutomationTestKit.Automations(s.BoardId)))!);
    }

    [Fact]
    public async Task A_rule_runs_in_the_same_request_and_reports_itself()
    {
        var s = await SetupAsync();
        await s.Owner.CreateAutomationAsync(s.BoardId,
            Rule(Trigger("card.completed"), [new { type = "move", listId = s.Done }, new { type = "priority", priority = "High" }], name: "Finish flow"));

        var response = await s.Board.Editor.PostEmptyAsync($"{s.CardUrl}/complete");

        response.EnsureSuccessStatusCode();
        var run = Assert.Single(response.Runs());
        Assert.Equal("Finish flow", run.RuleName);
        Assert.Equal("Moved to Done, set priority High", run.Summary);
        var card = await s.CardAsync();
        Assert.Equal(s.Done, card.ListId);
        Assert.Equal(CardPriority.High, card.Priority);
    }

    [Fact]
    public async Task Conditions_filter_which_cards_a_rule_runs_on()
    {
        var s = await SetupAsync();
        var bug = await s.LabelAsync("Bug");
        await s.Owner.CreateAutomationAsync(s.BoardId,
            Rule(Trigger("card.completed"), [new { type = "move", listId = s.Done }], [new { type = "label.any", labelIds = new[] { bug } }]));

        (await s.Owner.PostEmptyAsync($"{s.CardUrl}/complete")).EnsureSuccessStatusCode();
        Assert.Equal(s.Todo, (await s.CardAsync()).ListId);

        var other = await s.Owner.CreateCardAsync(s.BoardId, s.Todo, "Fix crash");
        (await s.Owner.PutAsJsonAsync($"{CardTestKit.Cards(s.BoardId)}/{other.Id}/labels", new { labelIds = new[] { bug } })).EnsureSuccessStatusCode();
        (await s.Owner.PostEmptyAsync($"{CardTestKit.Cards(s.BoardId)}/{other.Id}/complete")).EnsureSuccessStatusCode();
        Assert.Equal(s.Done, (await s.CardAsync(other.Id)).ListId);
    }

    [Fact]
    public async Task Changes_made_by_automations_trigger_no_further_automations()
    {
        var s = await SetupAsync();
        await s.Owner.CreateAutomationAsync(s.BoardId, Rule(Trigger("card.completed"), [new { type = "move", listId = s.Done }]));
        await s.Owner.CreateAutomationAsync(s.BoardId, Rule(Trigger("card.entered", s.Done), [new { type = "priority", priority = "Urgent" }]));

        var response = await s.Owner.PostEmptyAsync($"{s.CardUrl}/complete");

        Assert.Single(response.Runs());
        var card = await s.CardAsync();
        Assert.Equal(s.Done, card.ListId);
        Assert.Equal(CardPriority.None, card.Priority);
    }

    [Fact]
    public async Task Ticking_the_last_checklist_item_triggers_checklist_done()
    {
        var s = await SetupAsync();
        await s.Owner.CreateAutomationAsync(s.BoardId, Rule(Trigger("checklist.done"), [new { type = "move", listId = s.Review }]));
        var detail = new CardOnBoard(s.Board, s.Card.Id);
        var first = await detail.AddItemAsync(s.Owner, "Draft");
        var second = await detail.AddItemAsync(s.Owner, "Review");

        (await s.Owner.PutAsJsonAsync($"{s.CardUrl}/checklist/{first.Id}/done", new { isDone = true })).EnsureSuccessStatusCode();
        Assert.Equal(s.Todo, (await s.CardAsync()).ListId);

        var last = await s.Owner.PutAsJsonAsync($"{s.CardUrl}/checklist/{second.Id}/done", new { isDone = true });
        last.EnsureSuccessStatusCode();
        Assert.Equal(s.Review, (await s.CardAsync()).ListId);
    }

    [Fact]
    public async Task Adding_a_label_triggers_label_rules()
    {
        var s = await SetupAsync();
        var bug = await s.LabelAsync("Bug");
        await s.Owner.CreateAutomationAsync(s.BoardId, Rule(Trigger("label.added", labelId: bug), [new { type = "priority", priority = "High" }]));

        (await s.Owner.PutAsJsonAsync($"{s.CardUrl}/labels", new { labelIds = new[] { bug } })).EnsureSuccessStatusCode();

        Assert.Equal(CardPriority.High, (await s.CardAsync()).Priority);
    }

    [Fact]
    public async Task A_block_doorway_refuses_cards_that_miss_its_requirements()
    {
        var s = await SetupAsync();
        await s.Owner.CreateAutomationAsync(s.BoardId,
            Doorway(s.Review, "Enter", "Block", [new { type = "assignee.set" }], name: "Review gate"));

        var refused = await s.Board.Editor.MoveCardAsync(s.BoardId, s.Card, s.Review);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("doorway", problem.GetProperty("code").GetString());
        Assert.Equal("Review needs an assignee first.", problem.GetProperty("title").GetString());
        Assert.Equal(s.Todo, (await s.CardAsync()).ListId);

        var created = await s.Owner.PostAsJsonAsync($"/api/boards/{s.BoardId}/lists/{s.Review}/cards", new { title = "New" });
        Assert.Equal(HttpStatusCode.Conflict, created.StatusCode);

        var me = await s.Board.Editor.GetUserIdAsync();
        (await s.Owner.PutAsJsonAsync($"{s.CardUrl}/assignees", new { userIds = new[] { me } })).EnsureSuccessStatusCode();
        (await s.Board.Editor.MoveCardAsync(s.BoardId, await s.CardAsync(), s.Review)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Only_owners_can_move_a_card_through_a_block_doorway_anyway()
    {
        var s = await SetupAsync();
        await s.Owner.CreateAutomationAsync(s.BoardId, Doorway(s.Review, "Enter", "Block", [new { type = "assignee.set" }]));
        var body = new { listId = s.Review, rowVersion = s.Card.RowVersion, overrideDoorway = true };

        var editor = await s.Board.Editor.PutAsJsonAsync($"{s.CardUrl}/position", body);
        var owner = await s.Owner.PutAsJsonAsync($"{s.CardUrl}/position", body);

        Assert.Equal(HttpStatusCode.Conflict, editor.StatusCode);
        owner.EnsureSuccessStatusCode();
        Assert.Contains("doorway.overridden", await factory.ActivityTypesAsync(s.BoardId));
    }

    [Fact]
    public async Task A_leave_doorway_guards_cards_going_out()
    {
        var s = await SetupAsync();
        await s.Owner.CreateAutomationAsync(s.BoardId, Doorway(s.Todo, "Leave", "Block", [new { type = "due.set" }]));

        var response = await s.Owner.MoveCardAsync(s.BoardId, s.Card, s.Doing);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Cards leaving To do need a due date first.", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
    }

    [Fact]
    public async Task A_fix_doorway_fills_in_what_is_missing()
    {
        var s = await SetupAsync();
        await s.Owner.CreateAutomationAsync(s.BoardId,
            Doorway(s.Review, "Enter", "Fix", [new { type = "assignee.set" }, new { type = "priority.atLeast", priority = "High" }], name: "Review gate"));

        var response = await s.Board.Editor.MoveCardAsync(s.BoardId, s.Card, s.Review);

        response.EnsureSuccessStatusCode();
        Assert.Equal("Review gate", Assert.Single(response.Runs()).RuleName);
        var card = await s.CardAsync();
        Assert.Equal(s.Review, card.ListId);
        Assert.Equal(await s.Board.Editor.GetUserIdAsync(), Assert.Single(card.Assignees).UserId);
        Assert.Equal(CardPriority.High, card.Priority);
    }

    [Fact]
    public async Task A_warn_doorway_lets_the_card_through_unchanged()
    {
        var s = await SetupAsync();
        await s.Owner.CreateAutomationAsync(s.BoardId, Doorway(s.Review, "Enter", "Warn", [new { type = "assignee.set" }]));

        var response = await s.Owner.MoveCardAsync(s.BoardId, s.Card, s.Review);

        response.EnsureSuccessStatusCode();
        Assert.Empty(response.Runs());
        Assert.Empty((await s.CardAsync()).Assignees);
    }

    [Fact]
    public async Task Undo_reverts_a_run_once_for_the_person_who_triggered_it()
    {
        var s = await SetupAsync();
        var done = await s.LabelAsync("Done");
        await s.Owner.CreateAutomationAsync(s.BoardId,
            Rule(Trigger("card.completed"), [new { type = "move", listId = s.Done }, new { type = "label.add", labelId = done }]));
        var run = Assert.Single((await s.Board.Editor.PostEmptyAsync($"{s.CardUrl}/complete")).Runs());
        var undo = $"/api/boards/{s.BoardId}/automation-runs/{run.RunId}/undo";

        var viewer = await s.Board.Viewer.PostEmptyAsync(undo);
        var editor = await s.Board.Editor.PostEmptyAsync(undo);
        var again = await s.Board.Editor.PostEmptyAsync(undo);

        Assert.Equal(HttpStatusCode.Forbidden, viewer.StatusCode);
        editor.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        var card = await s.CardAsync();
        Assert.Equal(s.Todo, card.ListId);
        Assert.Empty(card.Labels);
        Assert.NotNull(card.CompletedAt);
        Assert.Contains("automation.undone", await factory.ActivityTypesAsync(s.BoardId));
    }

    [Fact]
    public async Task Undo_refuses_when_the_card_changed_since()
    {
        var s = await SetupAsync();
        await s.Owner.CreateAutomationAsync(s.BoardId, Rule(Trigger("card.completed"), [new { type = "move", listId = s.Done }]));
        var run = Assert.Single((await s.Owner.PostEmptyAsync($"{s.CardUrl}/complete")).Runs());
        (await s.Owner.MoveCardAsync(s.BoardId, await s.CardAsync(), s.Doing)).EnsureSuccessStatusCode();

        var response = await s.Owner.PostEmptyAsync($"/api/boards/{s.BoardId}/automation-runs/{run.RunId}/undo");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(s.Doing, (await s.CardAsync()).ListId);
    }

    [Fact]
    public async Task A_rule_that_refers_to_a_deleted_label_turns_itself_off()
    {
        var s = await SetupAsync();
        var bug = await s.LabelAsync("Bug");
        var rule = await s.Owner.CreateAutomationAsync(s.BoardId, Rule(Trigger("card.completed"), [new { type = "label.add", labelId = bug }]));
        (await s.Owner.DeleteAsync(new Uri($"/api/boards/{s.BoardId}/labels/{bug}", UriKind.Relative))).EnsureSuccessStatusCode();

        var listed = Assert.Single((await s.Owner.GetFromJsonAsync<List<AutomationView>>(AutomationTestKit.Automations(s.BoardId)))!);
        Assert.NotNull(listed.Problem);

        var response = await s.Owner.PostEmptyAsync($"{s.CardUrl}/complete");

        response.EnsureSuccessStatusCode();
        Assert.Empty(response.Runs());
        Assert.False(Assert.Single((await s.Owner.GetFromJsonAsync<List<AutomationView>>(AutomationTestKit.Automations(s.BoardId)))!).Enabled);
        Assert.Contains("automation.disabled", await factory.ActivityTypesAsync(s.BoardId));
        var toggle = await s.Owner.PutAsJsonAsync($"{AutomationTestKit.Automations(s.BoardId)}/{rule.Id}/enabled", new { enabled = true });
        Assert.Equal(HttpStatusCode.BadRequest, toggle.StatusCode);
    }

    [Fact]
    public async Task Hourly_rules_run_once_per_occurrence()
    {
        var s = await SetupAsync();
        var soon = await s.LabelAsync("Soon");
        await s.Owner.CreateAutomationAsync(s.BoardId, Rule(Trigger("due.soon", days: 2), [new { type = "label.add", labelId = soon }]));
        var now = DateTimeOffset.UtcNow;
        (await s.Owner.PatchAsJsonAsync(s.CardUrl, new { dueAt = now.AddDays(1) })).EnsureSuccessStatusCode();

        await factory.RunJobAsync("automation-timers", now);
        (await s.Owner.PutAsJsonAsync($"{s.CardUrl}/labels", new { labelIds = Array.Empty<Guid>() })).EnsureSuccessStatusCode();
        await factory.RunJobAsync("automation-timers", now.AddHours(1));

        Assert.Empty((await s.CardAsync()).Labels);
        var runs = 0;
        await factory.WithDbAsync(async db => runs = await db.AutomationRuns.CountAsync(r => r.BoardId == s.BoardId));
        Assert.Equal(1, runs);
    }

    [Fact]
    public async Task Cards_stuck_in_a_list_are_moved_by_the_hourly_job()
    {
        var s = await SetupAsync();
        await s.Owner.CreateAutomationAsync(s.BoardId, Rule(Trigger("list.stale", s.Todo, days: 3), [new { type = "move", listId = s.Doing }]));
        var now = DateTimeOffset.UtcNow;

        await factory.RunJobAsync("automation-timers", now);
        Assert.Equal(s.Todo, (await s.CardAsync()).ListId);

        await factory.WithDbAsync(db => db.Cards.Where(c => c.Id == s.Card.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.ListEnteredAt, now.AddDays(-4))));
        await factory.RunJobAsync("automation-timers", now);

        Assert.Equal(s.Doing, (await s.CardAsync()).ListId);
        Assert.Contains("card.moved", await factory.ActivityTypesAsync(s.BoardId));
    }
}
