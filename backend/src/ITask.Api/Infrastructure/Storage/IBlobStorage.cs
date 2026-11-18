namespace ITask.Api.Infrastructure.Storage;

/// <summary>Size and type of a stored blob.</summary>
public sealed record BlobInfo(long SizeBytes, string? ContentType);

/// <summary>A short-lived link straight to storage.</summary>
public sealed record SignedUrl(Uri Url, DateTimeOffset ExpiresAt);

/// <summary>
/// Private file storage. Browsers upload and download through short-lived signed URLs, so file
/// bytes never pass through the API.
/// </summary>
public interface IBlobStorage
{
    /// <summary>A URL that allows one PUT of <paramref name="blobName"/> until it expires.</summary>
    Task<SignedUrl> CreateUploadUrlAsync(string blobName, TimeSpan lifetime, CancellationToken ct);

    /// <summary>A read URL that downloads as <paramref name="fileName"/>.</summary>
    Task<SignedUrl> CreateDownloadUrlAsync(string blobName, string fileName, TimeSpan lifetime, CancellationToken ct);

    /// <summary>The blob's size and content type, or null when it doesn't exist.</summary>
    Task<BlobInfo?> GetInfoAsync(string blobName, CancellationToken ct);

    Task DeleteAsync(string blobName, CancellationToken ct);

    /// <summary>Deletes every blob whose name starts with <paramref name="prefix"/>.</summary>
    Task DeletePrefixAsync(string prefix, CancellationToken ct);
}
