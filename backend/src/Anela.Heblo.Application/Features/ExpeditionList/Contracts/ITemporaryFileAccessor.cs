namespace Anela.Heblo.Application.Features.ExpeditionList.Contracts;

public interface ITemporaryFileAccessor
{
    Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default);
    Task<string> CreateFromStreamAsync(Stream content, string fileExtension, CancellationToken cancellationToken = default);
    void DeleteIfExists(string path);
}
