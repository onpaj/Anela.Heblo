using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Anela.Heblo.Application.Features.ExpeditionList;
using Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.ReprintExpeditionList;
using Anela.Heblo.Application.Features.ExpeditionList.Services;
using Anela.Heblo.Domain.Features.FileStorage;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.ExpeditionListArchive;

public class ReprintExpeditionListHandlerTests
{
    private readonly Mock<IBlobStorageService> _blobStorageServiceMock;
    private readonly Mock<IPrintQueueSink> _cupsSinkMock;
    private readonly ReprintExpeditionListHandler _handler;
    private const string ContainerName = "expedition-lists";

    public ReprintExpeditionListHandlerTests()
    {
        _blobStorageServiceMock = new Mock<IBlobStorageService>();
        _cupsSinkMock = new Mock<IPrintQueueSink>();
        _handler = new ReprintExpeditionListHandler(_blobStorageServiceMock.Object, _cupsSinkMock.Object, Options.Create(new PrintPickingListOptions()));
    }

    [Fact]
    public async Task Handle_ValidBlobPath_DownloadsAndSendsToCupsSink()
    {
        // Arrange
        var blobPath = "2026-03-25/picking-list-001.pdf";
        var pdfContent = new byte[] { 0x25, 0x50, 0x44, 0x46 }; // PDF magic bytes
        var blobStream = new MemoryStream(pdfContent);

        _blobStorageServiceMock
            .Setup(s => s.DownloadAsync(ContainerName, blobPath, default))
            .ReturnsAsync(blobStream);

        _cupsSinkMock
            .Setup(s => s.SendAsync(It.IsAny<IEnumerable<string>>(), default))
            .Returns(Task.CompletedTask);

        var request = new ReprintExpeditionListRequest { BlobPath = blobPath };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.True(result.Success);
        _blobStorageServiceMock.Verify(s => s.DownloadAsync(ContainerName, blobPath, default), Times.Once);
        _cupsSinkMock.Verify(
            s => s.SendAsync(It.Is<IEnumerable<string>>(paths => paths.Any()), default),
            Times.Once);
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
        _blobStorageServiceMock.Verify(
            s => s.DownloadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _cupsSinkMock.Verify(
            s => s.SendAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_SuccessfulReprint_LeavesNoTempFileBehind()
    {
        // Arrange
        var blobPath = "2026-03-25/picking-list-002.pdf";
        var pdfContent = new byte[] { 0x25, 0x50, 0x44, 0x46 }; // PDF magic bytes
        var blobStream = new MemoryStream(pdfContent);

        _blobStorageServiceMock
            .Setup(s => s.DownloadAsync(ContainerName, blobPath, default))
            .ReturnsAsync(blobStream);

        string? capturedPath = null;
        bool capturedPathExistedDuringSinkCall = false;
        bool capturedPathEndsWithPdf = false;
        bool capturedPathRootedInTempDir = false;

        _cupsSinkMock
            .Setup(s => s.SendAsync(It.IsAny<IEnumerable<string>>(), default))
            .Callback<IEnumerable<string>, CancellationToken>((paths, _) =>
            {
                capturedPath = paths.Single();
                capturedPathExistedDuringSinkCall = File.Exists(capturedPath);
                capturedPathEndsWithPdf = capturedPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
                capturedPathRootedInTempDir = capturedPath.StartsWith(Path.GetTempPath(), StringComparison.Ordinal);
            })
            .Returns(Task.CompletedTask);

        var tempDir = Path.GetTempPath();
        var preCallFiles = new HashSet<string>(Directory.EnumerateFiles(tempDir));

        var request = new ReprintExpeditionListRequest { BlobPath = blobPath };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(capturedPath);
        Assert.True(capturedPathEndsWithPdf, "Path passed to sink must end in .pdf");
        Assert.True(capturedPathRootedInTempDir, "Path passed to sink must be rooted under Path.GetTempPath()");
        Assert.True(capturedPathExistedDuringSinkCall, "PDF file must exist on disk when sink is invoked");
        Assert.False(File.Exists(capturedPath), "Temp file must be deleted after handler returns");

        // Detect leaks: any *new* file in the temp dir whose name matches the
        // shapes the handler could plausibly produce (current buggy or fixed).
        var postCallFiles = Directory.EnumerateFiles(tempDir).ToList();
        var handlerArtifactPattern = new Regex(@"^(tmp[^/\\]*\.tmp(\.pdf)?|[0-9a-fA-F]{32}\.pdf)$");
        var leakedFiles = postCallFiles
            .Where(p => !preCallFiles.Contains(p))
            .Where(p => handlerArtifactPattern.IsMatch(Path.GetFileName(p)))
            .ToList();

        Assert.True(
            leakedFiles.Count == 0,
            $"Handler must not leave temp files behind. Found: {string.Join(", ", leakedFiles)}");
    }
}
