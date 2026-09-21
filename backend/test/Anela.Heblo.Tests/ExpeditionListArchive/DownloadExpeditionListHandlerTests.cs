using Anela.Heblo.Application.Features.ExpeditionListArchive;
using Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts;
using Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.DownloadExpeditionList;
using Anela.Heblo.Application.Shared;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.ExpeditionListArchive;

public class DownloadExpeditionListHandlerTests
{
    private readonly Mock<IExpeditionListArchiveBlobStore> _blobStoreMock;
    private readonly DownloadExpeditionListHandler _handler;
    private const string ContainerName = "expedition-lists";

    public DownloadExpeditionListHandlerTests()
    {
        _blobStoreMock = new Mock<IExpeditionListArchiveBlobStore>();
        _handler = new DownloadExpeditionListHandler(_blobStoreMock.Object, Options.Create(new ExpeditionListArchiveOptions()));
    }

    [Fact]
    public async Task Handle_ValidBlobPath_ReturnsBlobStream()
    {
        // Arrange
        var blobPath = "2026-03-25/picking-list-001.pdf";
        var expectedStream = new MemoryStream(new byte[] { 1, 2, 3 });

        _blobStoreMock
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
        Assert.Equal(ErrorCodes.InvalidBlobPath, result.ErrorCode);
        Assert.Null(result.Stream);

        _blobStoreMock.Verify(
            s => s.DownloadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
