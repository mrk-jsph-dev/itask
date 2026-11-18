using ITask.Api.Domain;

namespace ITask.Api.Common.Files;

/// <summary>Which files a card accepts and where their blobs live.</summary>
public static class AttachmentRules
{
    private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "text/plain",
        "text/csv",
        "text/markdown",
        "application/zip",
        "application/x-zip-compressed",
        "application/msword",
        "application/vnd.ms-excel",
        "application/vnd.ms-powerpoint",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "image/png",
        "image/jpeg",
        "image/gif",
        "image/webp",
        "image/avif",
        "image/bmp",
    };

    /// <summary>
    /// True for images, PDF, plain text, Office documents and zip. SVG is excluded because it can
    /// carry script. Parameters such as <c>; charset=utf-8</c> are ignored.
    /// </summary>
    public static bool IsAllowedType(string? contentType) =>
        !string.IsNullOrWhiteSpace(contentType) && AllowedTypes.Contains(MediaType(contentType));

    public static bool IsAllowedSize(long sizeBytes) => sizeBytes is > 0 and <= Attachment.MaxSizeBytes;

    /// <summary>The media type without parameters, lower case.</summary>
    public static string MediaType(string contentType)
    {
        ArgumentNullException.ThrowIfNull(contentType);
        var semicolon = contentType.IndexOf(';', StringComparison.Ordinal);
        return (semicolon >= 0 ? contentType[..semicolon] : contentType).Trim().ToLowerInvariant();
    }

    /// <summary>Prefix shared by every blob of a card.</summary>
    public static string CardPrefix(Guid boardId, Guid cardId) => $"{BoardPrefix(boardId)}cards/{cardId:N}/";

    /// <summary>Prefix shared by every blob of a board.</summary>
    public static string BoardPrefix(Guid boardId) => $"boards/{boardId:N}/";

    /// <summary>A fresh, unguessable blob name for a new file on the card.</summary>
    public static string NewBlobName(Guid boardId, Guid cardId) => $"{CardPrefix(boardId, cardId)}{Guid.NewGuid():N}";
}
