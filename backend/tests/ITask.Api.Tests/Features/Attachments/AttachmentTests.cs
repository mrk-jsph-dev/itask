using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ITask.Api.Common.Cards;
using ITask.Api.Features.Attachments.GetDownloadUrl;
using ITask.Api.Features.Attachments.RequestUpload;
using ITask.Api.Features.Cards.GetCard;
using ITask.Api.Tests.Infrastructure;

namespace ITask.Api.Tests.Features.Attachments;

public sealed class AttachmentTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static Task<HttpResponseMessage> RequestAsync(
        HttpClient client, CardOnBoard card, string fileName = "notes.pdf", string type = "application/pdf", long size = 4) =>
        client.PostAsJsonAsync($"{card.Url}/attachments/upload-url", new { fileName, contentType = type, sizeBytes = size });

    /// <summary>PUTs <paramref name="bytes"/> to a signed upload URL, as the browser would.</summary>
    private async Task<HttpResponseMessage> PutBlobAsync(Uri url, byte[] bytes, string type)
    {
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(type);
        using var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = content };
        request.Headers.Add("x-ms-blob-type", "BlockBlob");
        return await factory.CreateHttpsClient().SendAsync(request);
    }

    /// <summary>Request, upload and confirm; returns the confirm response.</summary>
    private async Task<HttpResponseMessage> UploadAsync(
        HttpClient client, CardOnBoard card, byte[] bytes, string type = "application/pdf", string fileName = "notes.pdf")
    {
        var requested = await RequestAsync(client, card, fileName, "application/pdf", 4);
        requested.EnsureSuccessStatusCode();
        var slot = (await requested.Content.ReadFromJsonAsync<RequestUpload.Response>())!;
        (await PutBlobAsync(slot.UploadUrl, bytes, type)).EnsureSuccessStatusCode();
        return await client.PostAsJsonAsync($"{card.Url}/attachments", new { blobName = slot.BlobName, fileName });
    }

    [Fact]
    public async Task Upload_confirm_list_and_download()
    {
        var card = await CardOnBoard.CreateAsync(factory);

        var confirmed = await UploadAsync(card.Board.Editor, card, "%PDF"u8.ToArray());
        var attachment = (await confirmed.Content.ReadFromJsonAsync<AttachmentView>())!;
        var detail = await card.Board.Viewer.GetFromJsonAsync<GetCard.Response>(card.Url);
        var link = await card.Board.Viewer.GetFromJsonAsync<GetDownloadUrl.Response>(
            $"{card.Url}/attachments/{attachment.Id}/url");
        var download = await factory.CreateHttpsClient().GetAsync(link!.Url);

        Assert.Equal(HttpStatusCode.Created, confirmed.StatusCode);
        Assert.Equal(("notes.pdf", "application/pdf", 4L), (attachment.FileName, attachment.ContentType, attachment.SizeBytes));
        Assert.Equal([attachment.Id], detail!.Attachments.Select(a => a.Id));
        Assert.Equal("%PDF"u8.ToArray(), await download.Content.ReadAsByteArrayAsync());
        Assert.Equal("notes.pdf", download.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
    }

    [Fact]
    public async Task Files_stay_on_their_own_card()
    {
        var card = await CardOnBoard.CreateAsync(factory);
        var todo = (await card.Board.Owner.GetListsAsync(card.BoardId))[0].Id;
        var sibling = await card.Board.Owner.CreateCardAsync(card.BoardId, todo, "Other card");
        (await UploadAsync(card.Board.Owner, card, [1, 2, 3])).EnsureSuccessStatusCode();

        var other = await card.Board.Owner.GetFromJsonAsync<GetCard.Response>($"{CardTestKit.Cards(card.BoardId)}/{sibling.Id}");

        Assert.Empty(other!.Attachments);
    }

    [Theory]
    [InlineData("evil.exe", "application/x-msdownload", 10)]
    [InlineData("vector.svg", "image/svg+xml", 10)]
    [InlineData("huge.pdf", "application/pdf", 10 * 1024 * 1024 + 1)]
    [InlineData("empty.pdf", "application/pdf", 0)]
    public async Task Request_rejects_wrong_type_and_size(string fileName, string type, long size)
    {
        var card = await CardOnBoard.CreateAsync(factory);

        var response = await RequestAsync(card.Board.Owner, card, fileName, type, size);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Confirm_checks_the_stored_size_and_type_and_deletes_bad_blobs()
    {
        var card = await CardOnBoard.CreateAsync(factory);

        var oversize = await UploadAsync(card.Board.Owner, card, new byte[(10 * 1024 * 1024) + 1]);
        var wrongType = await UploadAsync(card.Board.Owner, card, [1, 2, 3], "application/x-msdownload");

        Assert.Equal(HttpStatusCode.BadRequest, oversize.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, wrongType.StatusCode);
        Assert.DoesNotContain(
            Directory.EnumerateFiles(factory.BlobRoot, "*", SearchOption.AllDirectories),
            f => f.Contains(card.CardId.ToString("N"), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Confirm_rejects_missing_foreign_and_repeated_blobs()
    {
        var card = await CardOnBoard.CreateAsync(factory);
        var other = await CardOnBoard.CreateAsync(factory);
        var requested = await RequestAsync(other.Board.Owner, other);
        var slot = (await requested.Content.ReadFromJsonAsync<RequestUpload.Response>())!;
        (await PutBlobAsync(slot.UploadUrl, [1], "application/pdf")).EnsureSuccessStatusCode();
        var mine = await RequestAsync(card.Board.Owner, card);
        var unsent = (await mine.Content.ReadFromJsonAsync<RequestUpload.Response>())!;

        var foreign = await card.Board.Owner.PostAsJsonAsync(
            $"{card.Url}/attachments", new { blobName = slot.BlobName, fileName = "x.pdf" });
        var missing = await card.Board.Owner.PostAsJsonAsync(
            $"{card.Url}/attachments", new { blobName = unsent.BlobName, fileName = "x.pdf" });
        var first = await other.Board.Owner.PostAsJsonAsync(
            $"{other.Url}/attachments", new { blobName = slot.BlobName, fileName = "x.pdf" });
        var repeat = await other.Board.Owner.PostAsJsonAsync(
            $"{other.Url}/attachments", new { blobName = slot.BlobName, fileName = "x.pdf" });

        Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, repeat.StatusCode);
    }

    [Fact]
    public async Task Viewer_cannot_upload_and_stranger_gets_no_links()
    {
        var card = await CardOnBoard.CreateAsync(factory);
        var attachment = (await (await UploadAsync(card.Board.Owner, card, [1])).Content.ReadFromJsonAsync<AttachmentView>())!;

        var viewer = await RequestAsync(card.Board.Viewer, card);
        var stranger = await RequestAsync(card.Board.Stranger, card);
        var strangerLink = await card.Board.Stranger.GetAsync(
            new Uri($"{card.Url}/attachments/{attachment.Id}/url", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, viewer.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, stranger.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, strangerLink.StatusCode);
    }

    [Fact]
    public async Task Tampered_links_are_refused()
    {
        var card = await CardOnBoard.CreateAsync(factory);
        var requested = await RequestAsync(card.Board.Owner, card);
        var slot = (await requested.Content.ReadFromJsonAsync<RequestUpload.Response>())!;
        var tampered = new Uri(slot.UploadUrl.ToString().Replace("sig=", "sig=0", StringComparison.Ordinal), UriKind.Relative);
        var otherName = new Uri(slot.UploadUrl.ToString().Replace(card.CardId.ToString("N"), Guid.NewGuid().ToString("N"),
            StringComparison.Ordinal), UriKind.Relative);

        Assert.Equal(HttpStatusCode.Forbidden, (await PutBlobAsync(tampered, [1], "application/pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PutBlobAsync(otherName, [1], "application/pdf")).StatusCode);
    }

    [Fact]
    public async Task Uploader_or_owner_deletes_and_the_file_is_gone()
    {
        var card = await CardOnBoard.CreateAsync(factory);
        var byEditor = (await (await UploadAsync(card.Board.Editor, card, [1])).Content.ReadFromJsonAsync<AttachmentView>())!;
        var byOwner = (await (await UploadAsync(card.Board.Owner, card, [2])).Content.ReadFromJsonAsync<AttachmentView>())!;
        var link = await card.Board.Owner.GetFromJsonAsync<GetDownloadUrl.Response>($"{card.Url}/attachments/{byEditor.Id}/url");

        var editorDeletesOwners = await card.Board.Editor.DeleteAsync(new Uri($"{card.Url}/attachments/{byOwner.Id}", UriKind.Relative));
        var ownerDeletesEditors = await card.Board.Owner.DeleteAsync(new Uri($"{card.Url}/attachments/{byEditor.Id}", UriKind.Relative));
        var download = await factory.CreateHttpsClient().GetAsync(link!.Url);

        Assert.Equal(HttpStatusCode.Forbidden, editorDeletesOwners.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, ownerDeletesEditors.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);
    }

    [Fact]
    public async Task Deleting_the_board_removes_its_files()
    {
        var card = await CardOnBoard.CreateAsync(factory);
        (await UploadAsync(card.Board.Owner, card, [1])).EnsureSuccessStatusCode();
        await card.ArchiveBoardAsync();

        (await card.Board.Owner.DeleteAsync(new Uri($"/api/boards/{card.BoardId}", UriKind.Relative))).EnsureSuccessStatusCode();

        Assert.False(Directory.Exists(Path.Combine(factory.BlobRoot, "boards", card.BoardId.ToString("N"))));
    }
}

public sealed class DemoAttachmentTests(DemoApiFactory factory) : IClassFixture<DemoApiFactory>
{
    [Fact]
    public async Task Demo_editor_cannot_upload()
    {
        var client = factory.CreateHttpsClient();
        (await client.PostAsync(new Uri("/api/auth/demo/editor", UriKind.Relative), null)).EnsureSuccessStatusCode();
        var boardId = (await client.GetFromJsonAsync<List<ITask.Api.Features.Boards.GetMyBoards.GetMyBoards.Item>>("/api/boards"))![0].Id;
        var todo = (await client.GetListsAsync(boardId))[0].Id;
        var card = await client.CreateCardAsync(boardId, todo, "Demo card");

        var response = await client.PostAsJsonAsync(
            $"{CardTestKit.Cards(boardId)}/{card.Id}/attachments/upload-url",
            new { fileName = "a.pdf", contentType = "application/pdf", sizeBytes = 4 });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
