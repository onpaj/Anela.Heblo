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

    [Fact]
    public async Task CreateFromStreamAsync_ValidStream_CreatesFileWithMatchingContentAndExtension()
    {
        var contentBytes = new byte[] { 1, 2, 3, 4, 5 };
        using var stream = new MemoryStream(contentBytes);
        var accessor = new FileSystemTemporaryFileAccessor();

        var path = await accessor.CreateFromStreamAsync(stream, ".pdf");

        try
        {
            Assert.EndsWith(".pdf", path);
            Assert.StartsWith(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), path);
            Assert.Equal(contentBytes, await File.ReadAllBytesAsync(path));
        }
        finally
        {
            accessor.DeleteIfExists(path);
        }
    }

    [Fact]
    public async Task CreateFromStreamAsync_DifferentExtension_ReturnedPathEndsWithExtension()
    {
        var contentBytes = new byte[] { 9, 8, 7 };
        using var stream = new MemoryStream(contentBytes);
        var accessor = new FileSystemTemporaryFileAccessor();

        var path = await accessor.CreateFromStreamAsync(stream, ".zpl");

        try
        {
            Assert.EndsWith(".zpl", path);
        }
        finally
        {
            accessor.DeleteIfExists(path);
        }
    }

    [Fact]
    public async Task CreateFromStreamAsync_StreamThrows_LeavesNoPartialFileAndPropagatesException()
    {
        var accessor = new FileSystemTemporaryFileAccessor();
        using var throwingStream = new ThrowingStream();
        var filesBefore = Directory.GetFiles(Path.GetTempPath(), "*.pdf").ToHashSet();

        await Assert.ThrowsAsync<IOException>(() => accessor.CreateFromStreamAsync(throwingStream, ".pdf"));

        var filesAfter = Directory.GetFiles(Path.GetTempPath(), "*.pdf").ToHashSet();
        Assert.True(filesAfter.SetEquals(filesBefore), "No new .pdf file should remain in the temp directory after a failed CreateFromStreamAsync call.");
    }

    /// <summary>
    /// Test-only stream whose read operations always throw, used to simulate a failure
    /// mid-copy in <see cref="FileSystemTemporaryFileAccessor.CreateFromStreamAsync"/>.
    /// </summary>
    private sealed class ThrowingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
            => throw new IOException("Simulated stream read failure.");

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => throw new IOException("Simulated stream read failure.");

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
