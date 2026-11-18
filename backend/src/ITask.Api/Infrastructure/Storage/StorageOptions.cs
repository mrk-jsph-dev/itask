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

    public bool IsAzure => string.Equals(Provider, "Azure", StringComparison.OrdinalIgnoreCase);
}
