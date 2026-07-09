using Anela.Heblo.Application.Features.ExpeditionList.Contracts;
using ExpeditionListArchiveContracts = Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts;

namespace Anela.Heblo.Adapters.FileSystem.Features.ExpeditionList;

public class FileSystemTemporaryFileAccessor : ITemporaryFileAccessor, ExpeditionListArchiveContracts.ITemporaryFileAccessor
{
    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default)
        => File.ReadAllBytesAsync(path, cancellationToken);

    public async Task<string> CreateFromStreamAsync(Stream content, string fileExtension, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}{fileExtension}");
        try
        {
            await using var fileStream = File.Create(path);
            await content.CopyToAsync(fileStream, cancellationToken);
            return path;
        }
        catch
        {
            DeleteIfExists(path);
            throw;
        }
    }

    public void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}
