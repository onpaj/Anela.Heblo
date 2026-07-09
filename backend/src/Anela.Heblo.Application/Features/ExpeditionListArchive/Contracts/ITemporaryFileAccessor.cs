namespace Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts;

public interface ITemporaryFileAccessor
{
    Task<string> CreateFromStreamAsync(Stream content, string fileExtension, CancellationToken cancellationToken = default);
    void DeleteIfExists(string path);
}
