using System.Net;
using System.Net.Http.Json;
using ITask.Api.Common.Templates;
using ITask.Api.Domain;
using ITask.Api.Features.Boards.GetBoard;
using ITask.Api.Features.Templates.CreateBoardFromTemplate;
using ITask.Api.Features.Templates.ListTemplates;
using ITask.Api.Infrastructure.Templates;
using ITask.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace ITask.Api.Tests.Features.Templates;

public sealed class TemplateTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<CreateBoardFromTemplate.Response> CreateFromAsync(HttpClient client, string templateId, string name = "From template")
    {
        var response = await client.PostAsJsonAsync($"/api/templates/{templateId}/boards", new CreateBoardFromTemplate.Request(name));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreateBoardFromTemplate.Response>())!;
    }

    private static async Task<TemplateSummary> SaveAsync(HttpClient client, Guid boardId, string name, bool includeCards = true)
    {
        var response = await client.PostAsJsonAsync($"/api/boards/{boardId}/templates", new { name, includeCards });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TemplateSummary>())!;
    }

    [Fact]
    public void Built_in_templates_load_in_gallery_order_and_are_well_formed()
    {
        var catalog = factory.Services.GetRequiredService<ITemplateCatalog>();

        Assert.Equal(["kanban", "sprint", "personal", "bug-tracker", "cloud-ops"], catalog.All.Select(t => t.Id));
        Assert.All(catalog.All, t =>
        {
            var lists = t.Content.Lists.Select(l => l.Name).ToList();
            var labels = (t.Content.Labels ?? []).Select(l => l.Name).ToList();
            Assert.NotEmpty(lists);
            Assert.Equal(lists.Count, lists.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.All(t.Content.Cards ?? [], c =>
            {
                Assert.Contains(c.List, lists);
                Assert.All(c.Labels ?? [], l => Assert.Contains(l, labels));
            });
            Assert.Matches("^#[0-9A-F]{6}$", t.Color);
            Assert.All(t.Content.Labels ?? [], l => Assert.Matches("^#[0-9A-F]{6}$", l.Color));
        });
    }

    [Fact]
    public async Task List_returns_built_ins_and_only_my_saved_templates()
    {
        var me = await factory.CreateSignedInClientAsync();
        var other = await factory.CreateSignedInClientAsync();
        await SaveAsync(other, await other.CreateBoardAsync("Theirs"), "Their template");
        await SaveAsync(me, await me.CreateBoardAsync("Mine"), "My template");

        var list = await me.GetFromJsonAsync<ListTemplates.Response>("/api/templates");

        Assert.Equal(5, list!.BuiltIn.Count);
        Assert.Equal(TemplateSummary.BuiltIn, list.BuiltIn[0].Source);
        var mine = Assert.Single(list.Mine);
        Assert.Equal(("My template", TemplateSummary.Saved, 4), (mine.Name, mine.Source, mine.Lists.Count));
    }

    [Theory]
    [InlineData("kanban")]
    [InlineData("sprint")]
    [InlineData("personal")]
    [InlineData("bug-tracker")]
    [InlineData("cloud-ops")]
    public async Task Each_built_in_creates_its_lists_labels_and_cards(string id)
    {
        var client = await factory.CreateSignedInClientAsync();
        var template = factory.Services.GetRequiredService<ITemplateCatalog>().Find(id)!;

        var board = await CreateFromAsync(client, id, "Ops board");

        Assert.Equal((BoardRole.Owner, template.Emoji, template.Color), (board.Role, board.Emoji, board.Color));
        var lists = await client.GetListsAsync(board.Id);
        Assert.Equal(template.Content.Lists.Select(l => l.Name), lists.Select(l => l.Name));
        var cards = lists.SelectMany(l => l.Cards).ToList();
        Assert.Equal(template.Content.Cards!.Count, cards.Count);
        Assert.Equal(Enumerable.Range(1, cards.Count), cards.Select(c => c.Number).Order());
        Assert.All(cards, c => Assert.StartsWith("OB-", c.Key, StringComparison.Ordinal));
        var labelled = template.Content.Cards!.Where(c => c.Labels is { Count: > 0 }).Select(c => c.Title).ToHashSet();
        Assert.All(cards.Where(c => labelled.Contains(c.Title)), c => Assert.NotEmpty(c.Labels));
    }

    [Fact]
    public async Task Request_fields_override_the_template_defaults()
    {
        var client = await factory.CreateSignedInClientAsync();

        var response = await client.PostAsJsonAsync("/api/templates/kanban/boards", new CreateBoardFromTemplate.Request("Mine", "🚀", "Desc", "#0f766e"));
        var board = (await response.Content.ReadFromJsonAsync<CreateBoardFromTemplate.Response>())!;

        Assert.Equal(("🚀", "Desc", "#0F766E"), (board.Emoji, board.Description, board.Color));
    }

    [Fact]
    public async Task Unknown_or_foreign_templates_are_not_found_and_names_are_validated()
    {
        var me = await factory.CreateSignedInClientAsync();
        var other = await factory.CreateSignedInClientAsync();
        var theirs = await SaveAsync(other, await other.CreateBoardAsync(), "Private");

        var unknown = await me.PostAsJsonAsync("/api/templates/nope/boards", new CreateBoardFromTemplate.Request("X"));
        var foreign = await me.PostAsJsonAsync($"/api/templates/{theirs.Id}/boards", new CreateBoardFromTemplate.Request("X"));
        var blank = await me.PostAsJsonAsync("/api/templates/kanban/boards", new CreateBoardFromTemplate.Request(" "));

        Assert.Equal(
            (HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.BadRequest),
            (unknown.StatusCode, foreign.StatusCode, blank.StatusCode));
    }

    [Fact]
    public async Task Saved_template_copies_structure_but_not_people_dates_or_archived_items()
    {
        var card = await CardOnBoard.CreateAsync(factory);
        var owner = card.Board.Owner;
        var editorId = await card.Board.Editor.GetUserIdAsync();
        var lists = await owner.GetListsAsync(card.BoardId);
        (await owner.PatchAsJsonAsync(card.Url, new { dueAt = DateTimeOffset.UtcNow.AddDays(2), priority = "High" })).EnsureSuccessStatusCode();
        (await owner.PutAsJsonAsync($"{card.Url}/assignees", new { userIds = new[] { editorId } })).EnsureSuccessStatusCode();
        await owner.PostAsJsonAsync($"/api/boards/{card.BoardId}/labels", new { name = "Ops", color = "#0F766E" });
        var item = await card.AddItemAsync(owner, "Step");
        (await owner.PutAsJsonAsync($"{card.Url}/checklist/{item.Id}/done", new { isDone = true })).EnsureSuccessStatusCode();
        var gone = await owner.CreateCardAsync(card.BoardId, lists[1].Id, "Archived");
        await owner.PostEmptyAsync($"{CardTestKit.Cards(card.BoardId)}/{gone.Id}/archive");
        await owner.PostEmptyAsync($"/api/boards/{card.BoardId}/lists/{lists[3].Id}/archive");

        var saved = await SaveAsync(owner, card.BoardId, "  Team layout ");
        Assert.Equal(("Team layout", 3, 1, 1), (saved.Name, saved.Lists.Count, saved.LabelCount, saved.CardCount));

        var board = await CreateFromAsync(owner, saved.Id, "Copy");
        var copyLists = await owner.GetListsAsync(board.Id);
        var copy = Assert.Single(copyLists.SelectMany(l => l.Cards));
        var detail = await owner.GetFromJsonAsync<ITask.Api.Features.Cards.GetCard.GetCard.Response>($"{CardTestKit.Cards(board.Id)}/{copy.Id}");
        var members = await owner.GetFromJsonAsync<GetBoard.Response>($"/api/boards/{board.Id}");

        Assert.Equal(["To do", "In progress", "Review"], copyLists.Select(l => l.Name));
        Assert.Equal(("Write spec", CardPriority.High, (DateTimeOffset?)null), (copy.Title, copy.Priority, copy.DueAt));
        Assert.Empty(copy.Assignees);
        Assert.False(Assert.Single(detail!.Checklist).IsDone);
        Assert.NotNull(members);
    }

    [Fact]
    public async Task Without_cards_only_lists_and_labels_are_saved()
    {
        var card = await CardOnBoard.CreateAsync(factory);

        var saved = await SaveAsync(card.Board.Owner, card.BoardId, "Empty", includeCards: false);

        Assert.Equal((4, 0), (saved.Lists.Count, saved.CardCount));
    }

    [Theory]
    [InlineData("Editor", HttpStatusCode.Forbidden)]
    [InlineData("Viewer", HttpStatusCode.Forbidden)]
    [InlineData("Stranger", HttpStatusCode.NotFound)]
    public async Task Only_owners_may_save_a_board_as_template(string role, HttpStatusCode expected)
    {
        var board = await factory.CreateBoardWithMembersAsync();

        var response = await board.For(role).PostAsJsonAsync($"/api/boards/{board.BoardId}/templates", new { name = "T" });

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Save_validates_the_name_and_caps_templates_per_user()
    {
        var client = await factory.CreateSignedInClientAsync();
        var boardId = await client.CreateBoardAsync();

        var blank = await client.PostAsJsonAsync($"/api/boards/{boardId}/templates", new { name = "" });
        for (var i = 0; i < BoardTemplate.MaxPerUser; i++)
        {
            await SaveAsync(client, boardId, $"T{i}", includeCards: false);
        }

        var over = await client.PostAsJsonAsync($"/api/boards/{boardId}/templates", new { name = "One more" });

        Assert.Equal((HttpStatusCode.BadRequest, HttpStatusCode.Conflict), (blank.StatusCode, over.StatusCode));
    }

    [Fact]
    public async Task Delete_removes_only_my_own_templates()
    {
        var me = await factory.CreateSignedInClientAsync();
        var other = await factory.CreateSignedInClientAsync();
        var mine = await SaveAsync(me, await me.CreateBoardAsync(), "Mine");
        var theirs = await SaveAsync(other, await other.CreateBoardAsync(), "Theirs");

        var foreign = await me.DeleteAsync(new Uri($"/api/templates/{theirs.Id}", UriKind.Relative));
        var own = await me.DeleteAsync(new Uri($"/api/templates/{mine.Id}", UriKind.Relative));

        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NoContent), (foreign.StatusCode, own.StatusCode));
        Assert.Empty((await me.GetFromJsonAsync<ListTemplates.Response>("/api/templates"))!.Mine);
        Assert.Single((await other.GetFromJsonAsync<ListTemplates.Response>("/api/templates"))!.Mine);
    }

    [Fact]
    public async Task Creating_from_a_template_records_board_created_with_the_template_name()
    {
        var client = await factory.CreateSignedInClientAsync();

        var board = await CreateFromAsync(client, "sprint");

        Assert.Equal(["board.created"], await factory.ActivityTypesAsync(board.Id));
    }
}
