using System.Net.Http.Headers;
using System.Net.Http.Json;
using ITask.Api.Common.Activity;
using ITask.Api.Features.Attachments.RequestUpload;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Tests.Infrastructure;

/// <summary>Activity feed helpers.</summary>
public static class ActivityTestKit
{
    public static async Task<ActivityPage> GetActivityAsync(this HttpClient client, string url) =>
        (await client.GetFromJsonAsync<ActivityPage>(url))!;

    /// <summary>Activity types recorded on the board, oldest first, read straight from the database.</summary>
    public static async Task<List<string>> ActivityTypesAsync(this ApiFactory factory, Guid boardId)
    {
        var types = new List<string>();
        await factory.WithDbAsync(async db => types.AddRange(await db.Activity
            .Where(a => a.BoardId == boardId)
            .OrderBy(a => a.Id)
            .Select(a => a.Type)
            .ToListAsync()));
        return types;
    }

    /// <summary>Requests an upload slot, PUTs a small PDF and confirms it.</summary>
    public static async Task<HttpResponseMessage> UploadPdfAsync(
        this ApiFactory factory, HttpClient client, CardOnBoard card, string fileName = "notes.pdf")
    {
        var requested = await client.PostAsJsonAsync(
            $"{card.Url}/attachments/upload-url", new { fileName, contentType = "application/pdf", sizeBytes = 4 });
        requested.EnsureSuccessStatusCode();
        var slot = (await requested.Content.ReadFromJsonAsync<RequestUpload.Response>())!;

        using var content = new ByteArrayContent("%PDF"u8.ToArray());
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        using var put = new HttpRequestMessage(HttpMethod.Put, slot.UploadUrl) { Content = content };
        put.Headers.Add("x-ms-blob-type", "BlockBlob");
        (await factory.CreateHttpsClient().SendAsync(put)).EnsureSuccessStatusCode();

        return await client.PostAsJsonAsync($"{card.Url}/attachments", new { blobName = slot.BlobName, fileName });
    }
}
