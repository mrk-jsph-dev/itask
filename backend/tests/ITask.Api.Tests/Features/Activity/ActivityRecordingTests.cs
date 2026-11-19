using System.Net.Http.Json;
using ITask.Api.Common.Cards;
using ITask.Api.Features.Boards.GetBoard;
using ITask.Api.Tests.Infrastructure;

namespace ITask.Api.Tests.Features.Activity;

/// <summary>Every mutating slice writes exactly one activity entry of the expected type.</summary>
public sealed class ActivityRecordingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    /// <summary>A fresh board with a card, plus whatever a case's setup created.</summary>
    private sealed class Ctx(ApiFactory factory, CardOnBoard card, List<Guid> lists)
    {
        public ApiFactory Factory => factory;

        public CardOnBoard Card => card;

        public HttpClient Owner => card.Board.Owner;

        public HttpClient Editor => card.Board.Editor;

        public string Board => $"/api/boards/{card.BoardId}";

        public List<Guid> Lists => lists;

        /// <summary>Id of the thing the setup step created (label, checklist item, comment, attachment).</summary>
        public Guid Id { get; set; }

        public Guid Other { get; set; }

        public string Token { get; set; } = "";

        public async Task<CardSummary> SummaryAsync() => await card.SummaryAsync();

        public Task<HttpResponseMessage> Post(string url, object? body = null) =>
            body is null ? Owner.PostEmptyAsync(url) : Owner.PostAsJsonAsync(url, body);

        public static async Task<Guid> CreatedIdAsync(Task<HttpResponseMessage> request)
        {
            var response = await request;
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<IdOnly>())!.Id;
        }
    }

    private sealed record IdOnly(Guid Id);

    private sealed record Case(Func<Ctx, Task>? Setup, Func<Ctx, Task<HttpResponseMessage>> Act);

    private static readonly Dictionary<string, Case> Cases = new()
    {
        ["board.updated"] = new(null, async c =>
        {
            var board = (await c.Owner.GetFromJsonAsync<GetBoard.Response>(c.Board))!;
            return await c.Owner.PutAsJsonAsync(c.Board, new { name = "Renamed", board.Emoji, board.Description, board.Color, board.RowVersion });
        }),
        ["board.archived"] = new(null, c => c.Post($"{c.Board}/archive")),
        ["board.restored"] = new(c => c.Post($"{c.Board}/archive"), c => c.Post($"{c.Board}/restore")),
        ["member.joined"] = new(
            async c => c.Token = (await c.Owner.InviteAsync(c.Card.BoardId, Domain.BoardRole.Viewer)).Token,
            async c => await (await c.Factory.CreateSignedInClientAsync()).PostEmptyAsync($"/api/invites/{c.Token}/accept")),
        ["member.role_changed"] = new(
            async c => c.Id = await c.Editor.GetUserIdAsync(),
            c => c.Owner.PutAsJsonAsync($"{c.Board}/members/{c.Id}", new { role = "Viewer" })),
        ["member.removed"] = new(
            async c => c.Id = await c.Editor.GetUserIdAsync(),
            c => c.Owner.DeleteAsync(new Uri($"{c.Board}/members/{c.Id}", UriKind.Relative))),
        ["member.left"] = new(
            async c => c.Id = await c.Editor.GetUserIdAsync(),
            c => c.Editor.DeleteAsync(new Uri($"{c.Board}/members/{c.Id}", UriKind.Relative))),
        ["list.created"] = new(null, c => c.Post($"{c.Board}/lists", new { name = "QA" })),
        ["list.renamed"] = new(null, c => c.Owner.PutAsJsonAsync($"{c.Board}/lists/{c.Lists[1]}", new { name = "Doing" })),
        ["list.updated"] = new(null, async c =>
        {
            var list = (await c.Owner.GetListsAsync(c.Card.BoardId))[1];
            return await c.Owner.PutAsJsonAsync($"{c.Board}/lists/{list.Id}", new { name = list.Name, wipLimit = 3 });
        }),
        ["list.moved"] = new(null, c => c.Owner.PutAsJsonAsync(
            $"{c.Board}/lists/{c.Lists[0]}/position", new { beforeId = c.Lists[1], afterId = c.Lists[2] })),
        ["list.archived"] = new(null, c => c.Post($"{c.Board}/lists/{c.Lists[3]}/archive")),
        ["list.restored"] = new(c => c.Post($"{c.Board}/lists/{c.Lists[3]}/archive"), c => c.Post($"{c.Board}/lists/{c.Lists[3]}/restore")),
        ["card.created"] = new(null, c => c.Post($"{c.Board}/lists/{c.Lists[0]}/cards", new { title = "Another" })),
        ["card.updated"] = new(null, c => c.Owner.PatchAsJsonAsync(c.Card.Url, new { title = "Better title" })),
        ["card.moved"] = new(null, async c => await c.Owner.MoveCardAsync(c.Card.BoardId, await c.SummaryAsync(), c.Lists[1])),
        ["card.archived"] = new(null, c => c.Post($"{c.Card.Url}/archive")),
        ["card.restored"] = new(c => c.Post($"{c.Card.Url}/archive"), c => c.Post($"{c.Card.Url}/restore")),
        ["card.completed"] = new(null, c => c.Post($"{c.Card.Url}/complete")),
        ["card.reopened"] = new(c => c.Post($"{c.Card.Url}/complete"), c => c.Post($"{c.Card.Url}/reopen")),
        ["card.assignees_changed"] = new(
            async c => c.Id = await c.Owner.GetUserIdAsync(),
            c => c.Owner.PutAsJsonAsync($"{c.Card.Url}/assignees", new { userIds = new[] { c.Id } })),
        ["card.labels_changed"] = new(
            async c => c.Id = await Ctx.CreatedIdAsync(c.Post($"{c.Board}/labels", new { name = "Bug", color = "#DC2626" })),
            c => c.Owner.PutAsJsonAsync($"{c.Card.Url}/labels", new { labelIds = new[] { c.Id } })),
        ["card.recurrence_set"] = new(
            null, c => c.Owner.PutAsJsonAsync($"{c.Card.Url}/recurrence", new { kind = "Weekly", weekdays = new[] { 1 }, targetListId = c.Lists[0] })),
        ["card.recurrence_cleared"] = new(
            c => c.Owner.PutAsJsonAsync($"{c.Card.Url}/recurrence", new { kind = "Daily", targetListId = c.Lists[0] }),
            c => c.Owner.DeleteAsync(new Uri($"{c.Card.Url}/recurrence", UriKind.Relative))),
        ["label.created"] = new(null, c => c.Post($"{c.Board}/labels", new { name = "Bug", color = "#DC2626" })),
        ["label.updated"] = new(
            async c => c.Id = await Ctx.CreatedIdAsync(c.Post($"{c.Board}/labels", new { name = "Bug", color = "#DC2626" })),
            c => c.Owner.PutAsJsonAsync($"{c.Board}/labels/{c.Id}", new { name = "Defect", color = "#DC2626" })),
        ["label.deleted"] = new(
            async c => c.Id = await Ctx.CreatedIdAsync(c.Post($"{c.Board}/labels", new { name = "Bug", color = "#DC2626" })),
            c => c.Owner.DeleteAsync(new Uri($"{c.Board}/labels/{c.Id}", UriKind.Relative))),
        ["checklist.added"] = new(null, c => c.Post($"{c.Card.Url}/checklist", new { text = "Step" })),
        ["checklist.checked"] = new(
            async c => c.Id = (await c.Card.AddItemAsync(c.Owner, "Step")).Id,
            c => c.Owner.PutAsJsonAsync($"{c.Card.Url}/checklist/{c.Id}/done", new { isDone = true })),
        ["checklist.unchecked"] = new(
            async c =>
            {
                c.Id = (await c.Card.AddItemAsync(c.Owner, "Step")).Id;
                (await c.Owner.PutAsJsonAsync($"{c.Card.Url}/checklist/{c.Id}/done", new { isDone = true })).EnsureSuccessStatusCode();
            },
            c => c.Owner.PutAsJsonAsync($"{c.Card.Url}/checklist/{c.Id}/done", new { isDone = false })),
        ["checklist.updated"] = new(
            async c => c.Id = (await c.Card.AddItemAsync(c.Owner, "Step")).Id,
            c => c.Owner.PatchAsJsonAsync($"{c.Card.Url}/checklist/{c.Id}", new { text = "Better step" })),
        ["checklist.moved"] = new(
            async c =>
            {
                c.Id = (await c.Card.AddItemAsync(c.Owner, "First")).Id;
                c.Other = (await c.Card.AddItemAsync(c.Owner, "Second")).Id;
            },
            c => c.Owner.PutAsJsonAsync($"{c.Card.Url}/checklist/{c.Id}/position", new { beforeId = c.Other })),
        ["checklist.deleted"] = new(
            async c => c.Id = (await c.Card.AddItemAsync(c.Owner, "Step")).Id,
            c => c.Owner.DeleteAsync(new Uri($"{c.Card.Url}/checklist/{c.Id}", UriKind.Relative))),
        ["comment.added"] = new(null, c => c.Post($"{c.Card.Url}/comments", new { body = "Hi" })),
        ["comment.edited"] = new(
            async c => c.Id = await Ctx.CreatedIdAsync(c.Post($"{c.Card.Url}/comments", new { body = "Hi" })),
            c => c.Owner.PutAsJsonAsync($"{c.Card.Url}/comments/{c.Id}", new { body = "Hello" })),
        ["comment.deleted"] = new(
            async c => c.Id = await Ctx.CreatedIdAsync(c.Post($"{c.Card.Url}/comments", new { body = "Hi" })),
            c => c.Owner.DeleteAsync(new Uri($"{c.Card.Url}/comments/{c.Id}", UriKind.Relative))),
        ["attachment.added"] = new(null, c => c.Factory.UploadPdfAsync(c.Owner, c.Card)),
        ["attachment.deleted"] = new(
            async c => c.Id = await Ctx.CreatedIdAsync(c.Factory.UploadPdfAsync(c.Owner, c.Card)),
            c => c.Owner.DeleteAsync(new Uri($"{c.Card.Url}/attachments/{c.Id}", UriKind.Relative))),
    };

    public static TheoryData<string> Types => [.. Cases.Keys];

    [Theory]
    [MemberData(nameof(Types))]
    public async Task Each_change_records_exactly_one_entry(string type)
    {
        var card = await CardOnBoard.CreateAsync(factory);
        var lists = (await card.Board.Owner.GetListsAsync(card.BoardId)).Select(l => l.Id).ToList();
        var ctx = new Ctx(factory, card, lists);
        var test = Cases[type];
        if (test.Setup is not null)
        {
            await test.Setup(ctx);
        }

        var before = await factory.ActivityTypesAsync(card.BoardId);
        (await test.Act(ctx)).EnsureSuccessStatusCode();
        var after = await factory.ActivityTypesAsync(card.BoardId);

        Assert.Equal(before.Count + 1, after.Count);
        Assert.Equal(before.Count(t => t == type) + 1, after.Count(t => t == type));
    }

    [Fact]
    public async Task Creating_a_board_records_board_created()
    {
        var owner = await factory.CreateSignedInClientAsync();

        var boardId = await owner.CreateBoardAsync();

        Assert.Equal(["board.created"], await factory.ActivityTypesAsync(boardId));
    }

    [Fact]
    public async Task No_op_writes_record_nothing()
    {
        var card = await CardOnBoard.CreateAsync(factory);
        var before = await factory.ActivityTypesAsync(card.BoardId);

        (await card.Board.Owner.PatchAsJsonAsync(card.Url, new { title = "Write spec" })).EnsureSuccessStatusCode();
        (await card.Board.Owner.PostEmptyAsync($"{card.Url}/reopen")).EnsureSuccessStatusCode();
        (await card.Board.Owner.PutAsJsonAsync($"{card.Url}/labels", new { labelIds = Array.Empty<Guid>() })).EnsureSuccessStatusCode();

        Assert.Equal(before, await factory.ActivityTypesAsync(card.BoardId));
    }

    [Fact]
    public void Every_type_has_a_case()
    {
        var constants = typeof(ITask.Api.Common.Activity.ActivityTypes)
            .GetFields()
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!)
            .Where(t => t != "board.created");

        Assert.Empty(constants.Except(Cases.Keys));
    }
}
