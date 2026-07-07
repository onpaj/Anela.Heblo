using Anela.Heblo.Adapters.FileSystem.Features.ExpeditionList;
using Xunit;

namespace Anela.Heblo.Tests.Features.ExpeditionList;

public class FileSystemTemporaryFileAccessorTests : IDisposable
{
    private readonly string _testDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public FileSystemTemporaryFileAccessorTests()
    {
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir)) Directory.Delete(_testDir, recursive: true);
    }

    [Fact]
    public async Task ReadAllBytesAsync_ExistingFile_ReturnsFileContent()
    {
        var path = Path.Combine(_testDir, "file.pdf");
        var expectedBytes = new byte[] { 1, 2, 3, 4, 5 };
        await File.WriteAllBytesAsync(path, expectedBytes);

        var accessor = new FileSystemTemporaryFileAccessor();
        var bytes = await accessor.ReadAllBytesAsync(path);

        Assert.Equal(expectedBytes, bytes);
    }

    [Fact]
    public async Task ReadAllBytesAsync_MissingFile_ThrowsFileNotFoundException()
    {
        var path = Path.Combine(_testDir, "does-not-exist.pdf");
        var accessor = new FileSystemTemporaryFileAccessor();

        await Assert.ThrowsAsync<FileNotFoundException>(() => accessor.ReadAllBytesAsync(path));
    }

    [Fact]
    public void DeleteIfExists_ExistingFile_DeletesIt()
    {
        var path = Path.Combine(_testDir, "file.pdf");
        File.WriteAllText(path, "content");
        var accessor = new FileSystemTemporaryFileAccessor();

        accessor.DeleteIfExists(path);

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void DeleteIfExists_NonExistentFile_DoesNotThrow()
    {
        var path = Path.Combine(_testDir, "never-existed.pdf");
        var accessor = new FileSystemTemporaryFileAccessor();

        var exception = Record.Exception(() => accessor.DeleteIfExists(path));

        Assert.Null(exception);
    }
}
