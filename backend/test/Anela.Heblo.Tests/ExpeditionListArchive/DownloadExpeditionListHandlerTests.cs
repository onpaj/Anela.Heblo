using Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.DownloadExpeditionList;
using Anela.Heblo.Domain.Features.FileStorage;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.ExpeditionListArchive;

public class DownloadExpeditionListHandlerTests
{
    private readonly Mock<IBlobStorageService> _blobStorageServiceMock;
    private readonly DownloadExpeditionListHandler _handler;
    private const string ContainerName = "expedition-lists";

    public DownloadExpeditionListHandlerTests()
    {
        _blobStorageServiceMock = new Mock<IBlobStorageService>();
        _handler = new DownloadExpeditionListHandler(_blobStorageServiceMock.Object);
    }

    [Fact]
    public async Task Handle_ValidBlobPath_ReturnsBlobStream()
    {
        // Arrange
        var blobPath = "2026-03-25/picking-list-001.pdf";
        var expectedStream = new MemoryStream(new byte[] { 1, 2, 3 });

        _blobStorageServiceMock
            .Setup(s => s.DownloadAsync(ContainerName, blobPath, default))
            .ReturnsAsync(expectedStream);

        var request = new DownloadExpeditionListRequest { BlobPath = blobPath };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(expectedStream, result.Stream);
        Assert.Equal("application/pdf", result.ContentType);
        Assert.Equal("picking-list-001.pdf", result.FileName);
    }

    [Theory]
    [InlineData("../secret/file.pdf")]
    [InlineData("2026-03-25/../../../etc/passwd")]
    [InlineData("invalid-date/file.pdf")]
    [InlineData("2026-03-25/file.exe")]
    [InlineData("")]
    public async Task Handle_InvalidBlobPath_ReturnsFailure(string blobPath)
    {
        // Arrange
        var request = new DownloadExpeditionListRequest { BlobPath = blobPath };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.False(result.Success);
        Assert.Null(result.Stream);

        _blobStorageServiceMock.Verify(
            s => s.DownloadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
