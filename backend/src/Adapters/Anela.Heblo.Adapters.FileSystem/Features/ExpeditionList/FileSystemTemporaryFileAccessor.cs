using Anela.Heblo.Application.Features.ExpeditionList.Contracts;

namespace Anela.Heblo.Adapters.FileSystem.Features.ExpeditionList;

public class FileSystemTemporaryFileAccessor : ITemporaryFileAccessor
{
    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default)
        => File.ReadAllBytesAsync(path, cancellationToken);

    public void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}
