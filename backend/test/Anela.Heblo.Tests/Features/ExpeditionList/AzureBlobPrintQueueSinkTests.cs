using Anela.Heblo.Adapters.Azure.Features.ExpeditionList;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

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
}
