using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Extatic.Api.Storage;

public class AzureBlobStorageService : IBlobStorageService
{
    private readonly BlobContainerClient _container;

    public AzureBlobStorageService(IConfiguration configuration)
    {
        var connectionString = configuration["Storage:ConnectionString"]
            ?? throw new InvalidOperationException("Storage:ConnectionString is required");
        var containerName = configuration["Storage:ContainerName"] ?? "extatic-attachments";
        var serviceClient = new BlobServiceClient(connectionString);
        _container = serviceClient.GetBlobContainerClient(containerName);
        _container.CreateIfNotExists(PublicAccessType.Blob);
    }

    public async Task<string> UploadAsync(string path, Stream content, string contentType,
        CancellationToken ct = default)
    {
        var blob = _container.GetBlobClient(path);
        await blob.UploadAsync(content, new BlobHttpHeaders { ContentType = contentType }, cancellationToken: ct);
        return blob.Uri.ToString();
    }

    public async Task DeleteAsync(string path, CancellationToken ct = default)
    {
        var blob = _container.GetBlobClient(path);
        await blob.DeleteIfExistsAsync(cancellationToken: ct);
    }

    public async Task DeleteManyAsync(string prefix, CancellationToken ct = default)
    {
        await foreach (var blob in _container.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix, ct))
        {
            await _container.DeleteBlobIfExistsAsync(blob.Name, cancellationToken: ct);
        }
    }

    public string GetPublicUrl(string path)
        => _container.GetBlobClient(path).Uri.ToString();
}
