using Anela.Heblo.Adapters.Azure.Features.ExpeditionList;
using Azure;
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
        _containerClient.SetupGet(x => x.Name).Returns("print-queue");
        _containerClient.Setup(x => x.ExistsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(true, Mock.Of<Response>()));
        _containerClient
            .Setup(x => x.GetBlobClient(It.IsAny<string>()))
            .Returns(_blobClient.Object);
        _blobClient
            .Setup(x => x.UploadAsync(
                It.IsAny<Stream>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Response<BlobContentInfo>>());
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
        _containerClient.Verify(
            x => x.ExistsAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SendAsync_CalledTwice_ProbesContainerExistenceOnce()
    {
        // Arrange
        var file = Path.Combine(_tempDir, "order1.pdf");
        await File.WriteAllTextAsync(file, "pdf");

        var sink = CreateSink();

        // Act
        await sink.SendAsync([file]);
        await sink.SendAsync([file]);

        // Assert — ExistsAsync called once (cache hot after first call)
        _containerClient.Verify(
            x => x.ExistsAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SendAsync_FourParallelFirstCalls_ProbesContainerExistenceAtMostOnce()
    {
        // Arrange
        var file = Path.Combine(_tempDir, "order1.pdf");
        await File.WriteAllTextAsync(file, "pdf");

        // Block ExistsAsync until released so all 4 callers race
        var release = new TaskCompletionSource();
        _containerClient
            .Setup(x => x.ExistsAsync(It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await release.Task;
                return Response.FromValue(true, Mock.Of<Response>());
            });

        var sink = CreateSink();

        // Act — 4 concurrent SendAsync calls, then release the gate
        var calls = Enumerable.Range(0, 4)
            .Select(_ => Task.Run(() => sink.SendAsync([file])))
            .ToArray();

        await Task.Delay(50);
        release.SetResult();
        await Task.WhenAll(calls);

        // Assert
        _containerClient.Verify(
            x => x.ExistsAsync(It.IsAny<CancellationToken>()),
            Times.AtMostOnce());
    }

    [Fact]
    public async Task SendAsync_FirstExistsAsyncThrows_RetriesOnNextCall()
    {
        // Arrange
        var file = Path.Combine(_tempDir, "order1.pdf");
        await File.WriteAllTextAsync(file, "pdf");

        // First call to ExistsAsync throws; subsequent calls succeed.
        var attempt = 0;
        _containerClient
            .Setup(x => x.ExistsAsync(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                attempt++;
                if (attempt == 1)
                {
                    throw new InvalidOperationException("transient failure");
                }
                return Task.FromResult(Response.FromValue(true, Mock.Of<Response>()));
            });

        var sink = CreateSink();

        // Act + Assert — first call throws
        await Assert.ThrowsAsync<InvalidOperationException>(() => sink.SendAsync([file]));

        // Second call succeeds and triggers a retry of ExistsAsync (failed cache entry was evicted)
        await sink.SendAsync([file]);

        // Third call should NOT re-probe (cache is now hot)
        await sink.SendAsync([file]);

        // Assert — exactly 2 invocations total: the failed first, the successful second
        _containerClient.Verify(
            x => x.ExistsAsync(It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }
}
