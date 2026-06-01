using Anela.Heblo.Adapters.Azure.Features.ExpeditionList;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.ExpeditionList;

public class AzureBlobPrintQueueSinkTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    private readonly Mock<BlobContainerClient> _containerClient = new();
    private readonly Mock<BlobClient> _blobClient = new();

    public AzureBlobPrintQueueSinkTests()
    {
        Directory.CreateDirectory(_tempDir);
        _containerClient
            .Setup(x => x.GetBlobClient(It.IsAny<string>()))
            .Returns(_blobClient.Object);
        _containerClient
            .Setup(x => x.CreateIfNotExistsAsync(
                It.IsAny<PublicAccessType>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<BlobContainerEncryptionScopeOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Azure.Response<BlobContainerInfo>>());
        _blobClient
            .Setup(x => x.UploadAsync(
                It.IsAny<Stream>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Azure.Response<BlobContentInfo>>());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    private AzureBlobPrintQueueSink CreateSink() =>
        new AzureBlobPrintQueueSink(
            _containerClient.Object,
            TimeProvider.System,
            NullLogger<AzureBlobPrintQueueSink>.Instance);

    [Fact]
    public async Task SendAsync_ValidFiles_UploadsEachFileToBlob()
    {
        // Arrange
        var file1 = Path.Combine(_tempDir, "order1.pdf");
        var file2 = Path.Combine(_tempDir, "order2.pdf");
        await File.WriteAllTextAsync(file1, "pdf1");
        await File.WriteAllTextAsync(file2, "pdf2");

        var sink = CreateSink();

        // Act
        await sink.SendAsync([file1, file2]);

        // Assert
        _blobClient.Verify(x => x.UploadAsync(
            It.IsAny<Stream>(),
            true,
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task SendAsync_ValidFile_UsesBlobNameWithDatePrefix()
    {
        // Arrange
        var file = Path.Combine(_tempDir, "order1.pdf");
        await File.WriteAllTextAsync(file, "pdf");

        string? capturedBlobName = null;
        _containerClient
            .Setup(x => x.GetBlobClient(It.IsAny<string>()))
            .Callback<string>(name => capturedBlobName = name)
            .Returns(_blobClient.Object);

        var sink = CreateSink();

        // Act
        await sink.SendAsync([file]);

        // Assert
        Assert.NotNull(capturedBlobName);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}/order1\.pdf$", capturedBlobName);
    }

    [Fact]
    public async Task SendAsync_EmptyFilePaths_DoesNotUpload()
    {
        // Arrange
        var sink = CreateSink();

        // Act
        await sink.SendAsync([]);

        // Assert
        _blobClient.Verify(x => x.UploadAsync(
            It.IsAny<Stream>(),
            It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _containerClient.Verify(x => x.CreateIfNotExistsAsync(
            It.IsAny<PublicAccessType>(),
            It.IsAny<IDictionary<string, string>>(),
            It.IsAny<BlobContainerEncryptionScopeOptions>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendAsync_CalledTwice_InvokesCreateIfNotExistsOnce()
    {
        // Arrange
        var file = Path.Combine(_tempDir, "order1.pdf");
        await File.WriteAllTextAsync(file, "pdf");

        var sink = CreateSink();

        // Act
        await sink.SendAsync([file]);
        await sink.SendAsync([file]);

        // Assert
        _containerClient.Verify(x => x.CreateIfNotExistsAsync(
            It.IsAny<PublicAccessType>(),
            It.IsAny<IDictionary<string, string>>(),
            It.IsAny<BlobContainerEncryptionScopeOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendAsync_FourParallelFirstCalls_InvokesCreateIfNotExistsExactlyOnce()
    {
        // Arrange
        var file = Path.Combine(_tempDir, "order1.pdf");
        await File.WriteAllTextAsync(file, "pdf");

        // Block CreateIfNotExistsAsync until released, so all 4 callers race to the gate
        // at the same point in time. Without this, the first caller could complete before
        // the others even enter the method and the test would not exercise the gate.
        var release = new TaskCompletionSource();
        _containerClient
            .Setup(x => x.CreateIfNotExistsAsync(
                It.IsAny<PublicAccessType>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<BlobContainerEncryptionScopeOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await release.Task;
                return Mock.Of<Azure.Response<BlobContainerInfo>>();
            });

        var sink = CreateSink();

        // Act — fire 4 SendAsync calls in parallel, then release the gate
        var calls = Enumerable.Range(0, 4)
            .Select(_ => Task.Run(() => sink.SendAsync([file])))
            .ToArray();

        // Give the tasks time to converge on the semaphore before releasing
        await Task.Delay(50);
        release.SetResult();

        await Task.WhenAll(calls);

        // Assert
        _containerClient.Verify(x => x.CreateIfNotExistsAsync(
            It.IsAny<PublicAccessType>(),
            It.IsAny<IDictionary<string, string>>(),
            It.IsAny<BlobContainerEncryptionScopeOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendAsync_FirstCreateIfNotExistsThrows_RetriesOnNextCall()
    {
        // Arrange
        var file = Path.Combine(_tempDir, "order1.pdf");
        await File.WriteAllTextAsync(file, "pdf");

        // First call to CreateIfNotExistsAsync throws; subsequent calls succeed.
        var attempt = 0;
        _containerClient
            .Setup(x => x.CreateIfNotExistsAsync(
                It.IsAny<PublicAccessType>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<BlobContainerEncryptionScopeOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                attempt++;
                if (attempt == 1)
                {
                    throw new InvalidOperationException("transient failure");
                }
                return Task.FromResult(Mock.Of<Azure.Response<BlobContainerInfo>>());
            });

        var sink = CreateSink();

        // Act + Assert — first call throws
        await Assert.ThrowsAsync<InvalidOperationException>(() => sink.SendAsync([file]));

        // Second call succeeds and triggers a retry of CreateIfNotExistsAsync
        await sink.SendAsync([file]);

        // Third call should NOT re-invoke CreateIfNotExistsAsync (cache is now hot)
        await sink.SendAsync([file]);

        // Assert — exactly 2 invocations total: the failed first, the successful second
        _containerClient.Verify(x => x.CreateIfNotExistsAsync(
            It.IsAny<PublicAccessType>(),
            It.IsAny<IDictionary<string, string>>(),
            It.IsAny<BlobContainerEncryptionScopeOptions>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
