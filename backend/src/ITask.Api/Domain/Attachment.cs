namespace ITask.Api.Domain;

/// <summary>A file on a card. The bytes live in blob storage under <see cref="BlobName"/>.</summary>
public class Attachment
{
    public const int FileNameMaxLength = 255;

    public const int ContentTypeMaxLength = 127;

    public const int BlobNameMaxLength = 300;

    /// <summary>Most files one card can hold.</summary>
    public const int MaxPerCard = 20;

    /// <summary>Largest file accepted, in bytes (10 MB).</summary>
    public const long MaxSizeBytes = 10 * 1024 * 1024;

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid CardId { get; set; }

    public Card? Card { get; set; }

    public required string FileName { get; set; }

    public required string ContentType { get; set; }

    public long SizeBytes { get; set; }

    public required string BlobName { get; set; }

    public Guid UploadedById { get; set; }

    public AppUser? UploadedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
