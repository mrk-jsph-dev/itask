using System.Net;
using System.Net.Http.Json;
using ITask.Api.Common.Cards;
using ITask.Api.Tests.Infrastructure;

namespace ITask.Api.Tests.Features.Cards;

public sealed class CardEmojiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Emoji_is_set_kept_on_other_patches_and_cleared_with_null()
    {
        var card = await CardOnBoard.CreateAsync(factory);
        var owner = card.Board.Owner;

        var set = await owner.PatchAsJsonAsync(card.Url, new { emoji = "🖱️" });
        var other = await owner.PatchAsJsonAsync(card.Url, new { title = "Drag and drop" });
        var kept = (await other.Content.ReadFromJsonAsync<CardSummary>())!;
        var cleared = await owner.PatchAsJsonAsync(card.Url, new { emoji = (string?)null });

        Assert.Equal("🖱️", (await set.Content.ReadFromJsonAsync<CardSummary>())!.Emoji);
        Assert.Equal("🖱️", kept.Emoji);
        Assert.Null((await cleared.Content.ReadFromJsonAsync<CardSummary>())!.Emoji);
        Assert.Null((await card.SummaryAsync()).Emoji);
    }

    [Fact]
    public async Task Blank_emoji_clears_and_too_long_is_rejected()
    {
        var card = await CardOnBoard.CreateAsync(factory);
        var owner = card.Board.Owner;
        (await owner.PatchAsJsonAsync(card.Url, new { emoji = "🚀" })).EnsureSuccessStatusCode();

        var blank = await owner.PatchAsJsonAsync(card.Url, new { emoji = "  " });
        var tooLong = await owner.PatchAsJsonAsync(card.Url, new { emoji = new string('x', 17) });

        Assert.Null((await blank.Content.ReadFromJsonAsync<CardSummary>())!.Emoji);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    [Theory]
    [InlineData("Editor", HttpStatusCode.OK)]
    [InlineData("Viewer", HttpStatusCode.Forbidden)]
    [InlineData("Stranger", HttpStatusCode.NotFound)]
    public async Task Only_editors_set_the_emoji(string role, HttpStatusCode expected)
    {
        var card = await CardOnBoard.CreateAsync(factory);

        var response = await card.Board.For(role).PatchAsJsonAsync(card.Url, new { emoji = "🔥" });

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Attachment_count_includes_confirmed_uploads_only()
    {
        var card = await CardOnBoard.CreateAsync(factory);
        var owner = card.Board.Owner;

        (await owner.PostAsJsonAsync(
            $"{card.Url}/attachments/upload-url",
            new { fileName = "pending.pdf", contentType = "application/pdf", sizeBytes = 4 })).EnsureSuccessStatusCode();
        Assert.Equal(0, (await card.SummaryAsync()).AttachmentCount);

        (await factory.UploadPdfAsync(owner, card)).EnsureSuccessStatusCode();
        Assert.Equal(1, (await card.SummaryAsync()).AttachmentCount);
    }
}
