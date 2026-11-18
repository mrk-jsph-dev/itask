using ITask.Api.Common.Endpoints;
using Microsoft.Extensions.Options;

namespace ITask.Api.Infrastructure.Storage;

/// <summary>
/// Stand-in for the Blob Storage REST surface when the local provider is active: signed PUT to
/// upload and signed GET to download. Anonymous, like a SAS URL; the signature is the credential.
/// </summary>
internal sealed class LocalBlobEndpoints : IEndpoint
{
    public void Map(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (app.ServiceProvider.GetRequiredService<IOptions<StorageOptions>>().Value.IsAzure)
        {
            return;
        }

        var blobs = app.MapGroup("/local-blobs").AllowAnonymous().ExcludeFromDescription();
        blobs.MapPut("/{**blobName}", Upload);
        blobs.MapGet("/{**blobName}", Download);
    }

    private static async Task<IResult> Upload(
        string blobName, string? op, long? exp, string? sig, HttpRequest request, LocalBlobStorage storage, CancellationToken ct)
    {
        if (op != LocalBlobStorage.UploadOp || exp is null || !storage.Verify(op, blobName, exp.Value, null, sig))
        {
            return TypedResults.Problem(title: "Invalid or expired link.", statusCode: StatusCodes.Status403Forbidden);
        }

        if (!string.Equals(request.Headers["x-ms-blob-type"], "BlockBlob", StringComparison.Ordinal))
        {
            return TypedResults.Problem(title: "x-ms-blob-type must be BlockBlob.", statusCode: StatusCodes.Status400BadRequest);
        }

        return await storage.WriteAsync(blobName, request.Body, request.ContentType, ct)
            ? TypedResults.Created()
            : TypedResults.Problem(title: "The file is too large.", statusCode: StatusCodes.Status413PayloadTooLarge);
    }

    private static async Task<IResult> Download(
        string blobName, string? op, long? exp, string? sig, string? fn, LocalBlobStorage storage, CancellationToken ct)
    {
        if (op != LocalBlobStorage.DownloadOp || exp is null || !storage.Verify(op, blobName, exp.Value, fn, sig))
        {
            return TypedResults.Problem(title: "Invalid or expired link.", statusCode: StatusCodes.Status403Forbidden);
        }

        var info = await storage.GetInfoAsync(blobName, ct);
        if (info is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.PhysicalFile(
            storage.PathOf(blobName)!, info.ContentType ?? "application/octet-stream", fileDownloadName: fn);
    }
}
