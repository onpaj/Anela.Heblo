using Anela.Heblo.Application.Features.ExpeditionList.Contracts;
using Anela.Heblo.Application.Features.ExpeditionListArchive;
using Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.ReprintExpeditionList;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Application.Shared.Printing;
using Anela.Heblo.Domain.Features.FileStorage;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.ExpeditionListArchive;

public class ReprintExpeditionListHandlerTests
{
    private readonly Mock<IBlobStorageService> _blobStorageServiceMock;
    private readonly Mock<IPrintQueueSink> _cupsSinkMock;
    private readonly Mock<ITemporaryFileAccessor> _temporaryFileAccessorMock;
    private readonly ReprintExpeditionListHandler _handler;
    private const string ContainerName = "expedition-lists";

    public ReprintExpeditionListHandlerTests()
    {
        _blobStorageServiceMock = new Mock<IBlobStorageService>();
        _cupsSinkMock = new Mock<IPrintQueueSink>();
        _temporaryFileAccessorMock = new Mock<ITemporaryFileAccessor>();
        _handler = new ReprintExpeditionListHandler(
            _blobStorageServiceMock.Object,
            _cupsSinkMock.Object,
            _temporaryFileAccessorMock.Object,
            Options.Create(new ExpeditionListArchiveOptions()));
    }

    [Fact]
    public async Task Handle_ValidBlobPath_DownloadsAndSendsToCupsSink()
    {
        // Arrange
        var blobPath = "2026-03-25/picking-list-001.pdf";
        var pdfContent = new byte[] { 0x25, 0x50, 0x44, 0x46 }; // PDF magic bytes
        var blobStream = new MemoryStream(pdfContent);
        const string tempPath = "/tmp/generated-guid-001.pdf";

        _blobStorageServiceMock
            .Setup(s => s.DownloadAsync(ContainerName, blobPath, default))
            .ReturnsAsync(blobStream);

        _temporaryFileAccessorMock
            .Setup(a => a.CreateFromStreamAsync(blobStream, ".pdf", default))
            .ReturnsAsync(tempPath);

        _cupsSinkMock
            .Setup(s => s.SendAsync(It.IsAny<IEnumerable<string>>(), default))
            .Returns(Task.CompletedTask);

        var request = new ReprintExpeditionListRequest { BlobPath = blobPath };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.True(result.Success);
        _blobStorageServiceMock.Verify(s => s.DownloadAsync(ContainerName, blobPath, default), Times.Once);
        _temporaryFileAccessorMock.Verify(a => a.CreateFromStreamAsync(blobStream, ".pdf", default), Times.Once);
        _cupsSinkMock.Verify(
            s => s.SendAsync(It.Is<IEnumerable<string>>(paths => paths.Single() == tempPath), default),
            Times.Once);
    }

    [Fact]
    public async Task Handle_SuccessfulSend_DeletesTempFile()
    {
        // Arrange
        var blobPath = "2026-03-25/picking-list-002.pdf";
        var pdfContent = new byte[] { 0x25, 0x50, 0x44, 0x46 };
        var blobStream = new MemoryStream(pdfContent);
        const string tempPath = "/tmp/generated-guid-002.pdf";

        _blobStorageServiceMock
            .Setup(s => s.DownloadAsync(ContainerName, blobPath, default))
            .ReturnsAsync(blobStream);
        _temporaryFileAccessorMock
            .Setup(a => a.CreateFromStreamAsync(blobStream, ".pdf", default))
            .ReturnsAsync(tempPath);
        _cupsSinkMock
            .Setup(s => s.SendAsync(It.IsAny<IEnumerable<string>>(), default))
            .Returns(Task.CompletedTask);

        var request = new ReprintExpeditionListRequest { BlobPath = blobPath };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.True(result.Success);
        _temporaryFileAccessorMock.Verify(a => a.DeleteIfExists(tempPath), Times.Once);
    }

    [Fact]
    public async Task Handle_SendAsyncThrows_StillDeletesTempFileAndPropagates()
    {
        // Arrange
        var blobPath = "2026-03-25/picking-list-003.pdf";
        var blobStream = new MemoryStream(new byte[] { 0x25, 0x50, 0x44, 0x46 });
        const string tempPath = "/tmp/generated-guid-003.pdf";

        _blobStorageServiceMock
            .Setup(s => s.DownloadAsync(ContainerName, blobPath, default))
            .ReturnsAsync(blobStream);
        _temporaryFileAccessorMock
            .Setup(a => a.CreateFromStreamAsync(blobStream, ".pdf", default))
            .ReturnsAsync(tempPath);
        _cupsSinkMock
            .Setup(s => s.SendAsync(It.IsAny<IEnumerable<string>>(), default))
            .ThrowsAsync(new IOException("cups unavailable"));

        var request = new ReprintExpeditionListRequest { BlobPath = blobPath };

        // Act & Assert
        await Assert.ThrowsAsync<IOException>(() => _handler.Handle(request, default));

        _temporaryFileAccessorMock.Verify(a => a.DeleteIfExists(tempPath), Times.Once);
    }

    [Fact]
    public async Task Handle_BlobDownloadFails_CreatesNothing()
    {
        // Arrange
        var blobPath = "2026-03-25/picking-list-004.pdf";

        _blobStorageServiceMock
            .Setup(s => s.DownloadAsync(ContainerName, blobPath, default))
            .ThrowsAsync(new IOException("blob unavailable"));

        var request = new ReprintExpeditionListRequest { BlobPath = blobPath };

        // Act & Assert
        await Assert.ThrowsAsync<IOException>(() => _handler.Handle(request, default));

        _temporaryFileAccessorMock.Verify(
            a => a.CreateFromStreamAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _temporaryFileAccessorMock.Verify(
            a => a.DeleteIfExists(It.IsAny<string>()),
            Times.Never);
        _cupsSinkMock.Verify(
            s => s.SendAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_InvalidBlobPath_ReturnsFailureWithoutCallingBlob()
    {
        // Arrange
        var request = new ReprintExpeditionListRequest { BlobPath = "../malicious/path.pdf" };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.InvalidBlobPath, result.ErrorCode);
        _blobStorageServiceMock.Verify(
            s => s.DownloadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _cupsSinkMock.Verify(
            s => s.SendAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _temporaryFileAccessorMock.Verify(
            a => a.CreateFromStreamAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _temporaryFileAccessorMock.Verify(
            a => a.DeleteIfExists(It.IsAny<string>()),
            Times.Never);
    }
}
