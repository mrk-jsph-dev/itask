using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace ITask.Api.Infrastructure.Storage;

/// <summary>
/// Blobs as files under <see cref="StorageOptions.LocalRoot"/>, for development and tests. URLs
/// point at <see cref="LocalBlobEndpoints"/> and carry an HMAC signature over the operation, blob
/// name, expiry and file name, like a SAS. The key is random per process, so links die on restart.
/// </summary>
internal sealed partial class LocalBlobStorage : IBlobStorage
{
    public const string UploadOp = "w";
    public const string DownloadOp = "r";

    /// <summary>Largest body the local upload endpoint stores; confirm-time checks enforce the real limit.</summary>
    public const long MaxStoredBytes = 25 * 1024 * 1024;

    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private readonly string _root;
    private readonly TimeProvider _time;

    public LocalBlobStorage(IOptions<StorageOptions> options, IHostEnvironment env, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(env);
        _root = Path.GetFullPath(Path.Combine(env.ContentRootPath, options.Value.LocalRoot));
        _time = time;
    }

    public Task<SignedUrl> CreateUploadUrlAsync(string blobName, TimeSpan lifetime, CancellationToken ct) =>
        Task.FromResult(Sign(UploadOp, blobName, lifetime, null));

    public Task<SignedUrl> CreateDownloadUrlAsync(string blobName, string fileName, TimeSpan lifetime, CancellationToken ct) =>
        Task.FromResult(Sign(DownloadOp, blobName, lifetime, fileName));

    public async Task<BlobInfo?> GetInfoAsync(string blobName, CancellationToken ct)
    {
        var path = PathOf(blobName);
        if (path is null || !File.Exists(path))
        {
            return null;
        }

        var type = File.Exists(path + TypeSuffix) ? await File.ReadAllTextAsync(path + TypeSuffix, ct) : null;
        return new BlobInfo(new FileInfo(path).Length, type);
    }

    public Task DeleteAsync(string blobName, CancellationToken ct)
    {
        if (PathOf(blobName) is { } path)
        {
            File.Delete(path);
            File.Delete(path + TypeSuffix);
        }

        return Task.CompletedTask;
    }

    public Task DeletePrefixAsync(string prefix, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        if (prefix.EndsWith('/') && PathOf(prefix.TrimEnd('/')) is { } folder && Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }

        return Task.CompletedTask;
    }

    /// <summary>True when the signature matches and the link hasn't expired.</summary>
    public bool Verify(string op, string blobName, long expires, string? fileName, string? signature)
    {
        if (signature is null || _time.GetUtcNow().ToUnixTimeSeconds() > expires)
        {
            return false;
        }

        var expected = Encoding.ASCII.GetBytes(Signature(op, blobName, expires, fileName));
        return CryptographicOperations.FixedTimeEquals(expected, Encoding.ASCII.GetBytes(signature));
    }

    /// <summary>Stores <paramref name="content"/>; false when it exceeds <see cref="MaxStoredBytes"/>.</summary>
    public async Task<bool> WriteAsync(string blobName, Stream content, string? contentType, CancellationToken ct)
    {
        var path = PathOf(blobName) ?? throw new ArgumentException("Invalid blob name.", nameof(blobName));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var buffer = new byte[81920];
        long total = 0;
        await using (var file = File.Create(path))
        {
            int read;
            while ((read = await content.ReadAsync(buffer, ct)) > 0)
            {
                total += read;
                if (total > MaxStoredBytes)
                {
                    break;
                }

                await file.WriteAsync(buffer.AsMemory(0, read), ct);
            }
        }

        if (total > MaxStoredBytes)
        {
            await DeleteAsync(blobName, ct);
            return false;
        }

        await File.WriteAllTextAsync(path + TypeSuffix, contentType ?? "application/octet-stream", ct);
        return true;
    }

    /// <summary>The file for a blob, or null when the name is not a safe relative path.</summary>
    public string? PathOf(string blobName)
    {
        if (string.IsNullOrEmpty(blobName) || !SafeName().IsMatch(blobName))
        {
            return null;
        }

        var path = Path.GetFullPath(Path.Combine(_root, blobName));
        return path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? path : null;
    }

    private const string TypeSuffix = ".type";

    private SignedUrl Sign(string op, string blobName, TimeSpan lifetime, string? fileName)
    {
        var expiresAt = _time.GetUtcNow() + lifetime;
        var expires = expiresAt.ToUnixTimeSeconds();
        var query = $"op={op}&exp={expires.ToString(CultureInfo.InvariantCulture)}&sig={Signature(op, blobName, expires, fileName)}";
        if (fileName is not null)
        {
            query += $"&fn={Uri.EscapeDataString(fileName)}";
        }

        return new SignedUrl(new Uri($"/api/local-blobs/{blobName}?{query}", UriKind.Relative), expiresAt);
    }

    private string Signature(string op, string blobName, long expires, string? fileName)
    {
        var payload = Encoding.UTF8.GetBytes($"{op}\n{blobName}\n{expires.ToString(CultureInfo.InvariantCulture)}\n{fileName}");
        return Convert.ToHexStringLower(HMACSHA256.HashData(_key, payload));
    }

    // Lower-case segments of letters, digits and dashes separated by single slashes.
    [GeneratedRegex("^[a-z0-9-]+(/[a-z0-9-]+)*$")]
    private static partial Regex SafeName();
}
