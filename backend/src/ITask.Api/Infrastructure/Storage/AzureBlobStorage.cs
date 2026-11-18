using System.Runtime.CompilerServices;
using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Options;

namespace ITask.Api.Infrastructure.Storage;

/// <summary>
/// Azure Blob Storage through the app's managed identity. SAS links are user-delegation SAS, so
/// no account key exists anywhere.
/// </summary>
internal sealed class AzureBlobStorage : IBlobStorage, IDisposable
{
    private static readonly TimeSpan KeyLifetime = TimeSpan.FromHours(2);
    private static readonly TimeSpan KeyRefreshMargin = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);

    private readonly BlobServiceClient _service;
    private readonly BlobContainerClient _container;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _keyLock = new(1, 1);
    private UserDelegationKey? _key;

    public AzureBlobStorage(IOptions<StorageOptions> options, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(options);
        var endpoint = options.Value.BlobEndpoint
            ?? throw new InvalidOperationException("Storage:BlobEndpoint is not configured.");
        _service = new BlobServiceClient(new Uri(endpoint), new DefaultAzureCredential());
        _container = _service.GetBlobContainerClient(options.Value.Container);
        _time = time;
    }

    public Task<SignedUrl> CreateUploadUrlAsync(string blobName, TimeSpan lifetime, CancellationToken ct) =>
        SignAsync(blobName, lifetime, BlobSasPermissions.Create | BlobSasPermissions.Write, null, ct);

    public Task<SignedUrl> CreateDownloadUrlAsync(string blobName, string fileName, TimeSpan lifetime, CancellationToken ct) =>
        SignAsync(blobName, lifetime, BlobSasPermissions.Read, ContentDisposition.Attachment(fileName), ct);

    public async Task<BlobInfo?> GetInfoAsync(string blobName, CancellationToken ct)
    {
        try
        {
            BlobProperties props = await _container.GetBlobClient(blobName).GetPropertiesAsync(cancellationToken: ct);
            return new BlobInfo(props.ContentLength, props.ContentType);
        }
        catch (RequestFailedException e) when (e.Status == StatusCodes.Status404NotFound)
        {
            return null;
        }
    }

    public Task DeleteAsync(string blobName, CancellationToken ct) =>
        _container.GetBlobClient(blobName).DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: ct);

    public async Task DeletePrefixAsync(string prefix, CancellationToken ct)
    {
        await foreach (var blob in _container.GetBlobsAsync(new GetBlobsOptions { Prefix = prefix }, ct))
        {
            await DeleteAsync(blob.Name, ct);
        }
    }

    public async IAsyncEnumerable<string> ListNamesAsync(
        DateTimeOffset modifiedBefore, [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var blob in _container.GetBlobsAsync(new GetBlobsOptions(), ct))
        {
            if (blob.Properties.LastModified is { } modified && modified < modifiedBefore)
            {
                yield return blob.Name;
            }
        }
    }

    public void Dispose() => _keyLock.Dispose();

    private async Task<SignedUrl> SignAsync(
        string blobName, TimeSpan lifetime, BlobSasPermissions permissions, string? disposition, CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        var expires = now + lifetime;
        var builder = new BlobSasBuilder
        {
            BlobContainerName = _container.Name,
            BlobName = blobName,
            Resource = "b",
            StartsOn = now - ClockSkew,
            ExpiresOn = expires,
            Protocol = SasProtocol.Https,
            ContentDisposition = disposition,
        };
        builder.SetPermissions(permissions);

        var key = await GetKeyAsync(now, ct);
        var uri = new BlobUriBuilder(_container.GetBlobClient(blobName).Uri)
        {
            Sas = builder.ToSasQueryParameters(key, _service.AccountName),
        };
        return new SignedUrl(uri.ToUri(), expires);
    }

    private async Task<UserDelegationKey> GetKeyAsync(DateTimeOffset now, CancellationToken ct)
    {
        if (_key is { } cached && cached.SignedExpiresOn - now > KeyRefreshMargin)
        {
            return cached;
        }

        await _keyLock.WaitAsync(ct);
        try
        {
            if (_key is null || _key.SignedExpiresOn - now <= KeyRefreshMargin)
            {
                _key = (await _service.GetUserDelegationKeyAsync(now - ClockSkew, now + KeyLifetime, ct)).Value;
            }

            return _key;
        }
        finally
        {
            _keyLock.Release();
        }
    }
}
