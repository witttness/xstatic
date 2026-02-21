namespace Extatic.Api.Storage;

public interface IBlobStorageService
{
    Task<string> UploadAsync(string path, Stream content, string contentType, CancellationToken ct = default);
    Task DeleteAsync(string path, CancellationToken ct = default);
    Task DeleteManyAsync(string prefix, CancellationToken ct = default);
    string GetPublicUrl(string path);
}
