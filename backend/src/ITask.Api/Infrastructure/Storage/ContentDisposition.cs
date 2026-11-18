using System.Text;

namespace ITask.Api.Infrastructure.Storage;

internal static class ContentDisposition
{
    /// <summary>
    /// <c>attachment</c> with an ASCII fallback name and the full name as RFC 5987 <c>filename*</c>.
    /// </summary>
    public static string Attachment(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        var ascii = new StringBuilder(fileName.Length);
        foreach (var c in fileName)
        {
            ascii.Append(c is >= ' ' and < (char)127 and not '"' and not '\\' ? c : '_');
        }

        return $"attachment; filename=\"{ascii}\"; filename*=UTF-8''{Uri.EscapeDataString(fileName)}";
    }
}
