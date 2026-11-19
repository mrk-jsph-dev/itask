namespace ITask.Api.Infrastructure.Storage;

public sealed class StorageOptions
{
    public const string Section = "Storage";

    /// <summary>"Local" (files on disk, development and tests) or "Azure" (Blob Storage).</summary>
    public string Provider { get; set; } = "Local";

    /// <summary>Blob service endpoint, e.g. https://account.blob.core.windows.net/. Azure only.</summary>
    public string? BlobEndpoint { get; set; }

    public string Container { get; set; } = "attachments";

    /// <summary>Folder for the local provider; relative paths resolve from the content root.</summary>
    public string LocalRoot { get; set; } = "App_Data/blobs";

    /// <summary>Most attachment bytes one user can have stored.</summary>
    public long QuotaBytesPerUser { get; set; } = 100L * 1024 * 1024;

    /// <summary>
    /// Most upload URLs one user gets per UTC day. Blob Storage can't limit the size of a direct PUT, so this
    /// bounds what unconfirmed uploads (deleted after a day by the orphan-uploads job) can pile up.
    /// </summary>
    public int UploadUrlsPerUserPerDay { get; set; } = 50;

    /// <summary>Most attachment bytes stored across the whole app.</summary>
    public long QuotaBytesTotal { get; set; } = 2L * 1024 * 1024 * 1024;

    public bool IsAzure => string.Equals(Provider, "Azure", StringComparison.OrdinalIgnoreCase);
}
