using Anela.Heblo.Application.Features.ExpeditionList.Services;
using Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.DownloadExpeditionList;
using Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.GetExpeditionDates;
using Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.GetExpeditionListsByDate;
using Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.ReprintExpeditionList;
using Anela.Heblo.Tests.Features.FileStorage;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.ExpeditionListArchive;

public class GetExpeditionDatesHandlerTests
{
    private readonly MockBlobStorageService _blobStorage = new();
    private readonly Mock<ILogger<GetExpeditionDatesHandler>> _logger = new();
    private readonly GetExpeditionDatesHandler _handler;

    public GetExpeditionDatesHandlerTests()
    {
        _handler = new GetExpeditionDatesHandler(_blobStorage, _logger.Object);
    }

    private void SeedBlobs(params string[] blobNames)
    {
        foreach (var name in blobNames)
        {
            _blobStorage.UploadAsync(new MemoryStream(), "expedition-lists", name, "application/pdf").GetAwaiter().GetResult();
        }
    }

    [Fact]
    public async Task Handle_NoBlobsExist_ReturnsEmptyDates()
    {
        var result = await _handler.Handle(new GetExpeditionDatesRequest { Page = 1, PageSize = 10 }, CancellationToken.None);

        Assert.Empty(result.Dates);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task Handle_BlobsWithDates_ReturnsUniqueDistinctDates()
    {
        SeedBlobs(
            "2026-03-25/list-a.pdf",
            "2026-03-25/list-b.pdf",
            "2026-03-24/list-c.pdf"
        );

        var result = await _handler.Handle(new GetExpeditionDatesRequest { Page = 1, PageSize = 10 }, CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.Dates.Count);
    }

    [Fact]
    public async Task Handle_BlobsWithDates_ReturnsDatesDescending()
    {
        SeedBlobs(
            "2026-03-23/list-a.pdf",
            "2026-03-25/list-b.pdf",
            "2026-03-24/list-c.pdf"
        );

        var result = await _handler.Handle(new GetExpeditionDatesRequest { Page = 1, PageSize = 10 }, CancellationToken.None);

        Assert.Equal("2026-03-25", result.Dates[0]);
        Assert.Equal("2026-03-24", result.Dates[1]);
        Assert.Equal("2026-03-23", result.Dates[2]);
    }

    [Fact]
    public async Task Handle_Pagination_ReturnsCorrectPage()
    {
        SeedBlobs(
            "2026-03-25/a.pdf",
            "2026-03-24/b.pdf",
            "2026-03-23/c.pdf",
            "2026-03-22/d.pdf",
            "2026-03-21/e.pdf"
        );

        var result = await _handler.Handle(new GetExpeditionDatesRequest { Page = 2, PageSize = 2 }, CancellationToken.None);

        Assert.Equal(5, result.TotalCount);
        Assert.Equal(2, result.Dates.Count);
        Assert.Equal("2026-03-23", result.Dates[0]);
        Assert.Equal("2026-03-22", result.Dates[1]);
    }

    [Fact]
    public async Task Handle_BlobWithInvalidDatePrefix_ExcludedFromResults()
    {
        SeedBlobs(
            "2026-03-25/valid.pdf",
            "invalid-prefix/list.pdf",
            "nodatehere.pdf"
        );

        var result = await _handler.Handle(new GetExpeditionDatesRequest { Page = 1, PageSize = 10 }, CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("2026-03-25", result.Dates[0]);
    }

    [Fact]
    public async Task Handle_ReturnsCorrectPageMetadata()
    {
        SeedBlobs("2026-03-25/a.pdf");

        var result = await _handler.Handle(new GetExpeditionDatesRequest { Page = 1, PageSize = 5 }, CancellationToken.None);

        Assert.Equal(1, result.Page);
        Assert.Equal(5, result.PageSize);
    }
}

public class GetExpeditionListsByDateHandlerTests
{
    private readonly MockBlobStorageService _blobStorage = new();
    private readonly Mock<ILogger<GetExpeditionListsByDateHandler>> _logger = new();
    private readonly GetExpeditionListsByDateHandler _handler;

    public GetExpeditionListsByDateHandlerTests()
    {
        _handler = new GetExpeditionListsByDateHandler(_blobStorage, _logger.Object);
    }

    private void SeedBlob(string name)
    {
        _blobStorage.UploadAsync(new MemoryStream(), "expedition-lists", name, "application/pdf").GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Handle_DateWithBlobs_ReturnsItems()
    {
        SeedBlob("2026-03-25/list-a.pdf");
        SeedBlob("2026-03-25/list-b.pdf");

        var result = await _handler.Handle(new GetExpeditionListsByDateRequest { Date = "2026-03-25" }, CancellationToken.None);

        Assert.Equal("2026-03-25", result.Date);
        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public async Task Handle_DateWithBlobs_ItemsHaveCorrectFields()
    {
        SeedBlob("2026-03-25/picking-list.pdf");

        var result = await _handler.Handle(new GetExpeditionListsByDateRequest { Date = "2026-03-25" }, CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal("picking-list.pdf", item.FileName);
        Assert.Equal("2026-03-25/picking-list.pdf", item.BlobPath);
    }

    [Fact]
    public async Task Handle_DateWithNoBlobs_ReturnsEmptyList()
    {
        SeedBlob("2026-03-24/other.pdf");

        var result = await _handler.Handle(new GetExpeditionListsByDateRequest { Date = "2026-03-25" }, CancellationToken.None);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Handle_OnlyReturnsBlobsForRequestedDate()
    {
        SeedBlob("2026-03-25/match.pdf");
        SeedBlob("2026-03-24/no-match.pdf");

        var result = await _handler.Handle(new GetExpeditionListsByDateRequest { Date = "2026-03-25" }, CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Equal("match.pdf", result.Items[0].FileName);
    }
}

public class DownloadExpeditionListHandlerTests
{
    private readonly MockBlobStorageService _blobStorage = new();
    private readonly Mock<ILogger<DownloadExpeditionListHandler>> _logger = new();
    private readonly DownloadExpeditionListHandler _handler;

    public DownloadExpeditionListHandlerTests()
    {
        _handler = new DownloadExpeditionListHandler(_blobStorage, _logger.Object);
    }

    [Theory]
    [InlineData("2026-03-25/list.pdf")]
    [InlineData("2020-01-01/expedition-list-morning.pdf")]
    [InlineData("1999-12-31/a.pdf")]
    public async Task Handle_ValidBlobPath_ReturnsStreamAndMetadata(string blobPath)
    {
        var result = await _handler.Handle(new DownloadExpeditionListRequest { BlobPath = blobPath }, CancellationToken.None);

        Assert.NotNull(result.Stream);
        Assert.Equal("application/pdf", result.ContentType);
        Assert.Equal(Path.GetFileName(blobPath), result.FileName);
    }

    [Theory]
    [InlineData("../etc/passwd")]
    [InlineData("../../secret.pdf")]
    [InlineData("2026-03-25/list.exe")]
    [InlineData("2026-03-25/sub/dir/list.pdf")]
    [InlineData("notadate/list.pdf")]
    [InlineData("2026-03-25/")]
    [InlineData("")]
    [InlineData("2026-03-25/list.pdf.exe")]
    public async Task Handle_InvalidBlobPath_ThrowsArgumentException(string blobPath)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _handler.Handle(new DownloadExpeditionListRequest { BlobPath = blobPath }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_ValidPath_ReturnsCorrectFileName()
    {
        var result = await _handler.Handle(
            new DownloadExpeditionListRequest { BlobPath = "2026-03-25/my-list.pdf" },
            CancellationToken.None);

        Assert.Equal("my-list.pdf", result.FileName);
    }
}

public class ReprintExpeditionListHandlerTests
{
    private readonly MockBlobStorageService _blobStorage = new();
    private readonly Mock<IPrintQueueSink> _printQueueSink = new();
    private readonly Mock<ILogger<ReprintExpeditionListHandler>> _logger = new();
    private readonly ReprintExpeditionListHandler _handler;

    public ReprintExpeditionListHandlerTests()
    {
        _printQueueSink
            .Setup(x => x.SendAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _handler = new ReprintExpeditionListHandler(_blobStorage, _printQueueSink.Object, _logger.Object);
    }

    [Fact]
    public async Task Handle_ValidBlobPath_ReturnsSuccess()
    {
        var result = await _handler.Handle(
            new ReprintExpeditionListRequest { BlobPath = "2026-03-25/list.pdf" },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotEmpty(result.Message);
    }

    [Fact]
    public async Task Handle_ValidBlobPath_SendsToPrintQueue()
    {
        await _handler.Handle(
            new ReprintExpeditionListRequest { BlobPath = "2026-03-25/list.pdf" },
            CancellationToken.None);

        _printQueueSink.Verify(
            x => x.SendAsync(It.Is<IEnumerable<string>>(files => files.Count() == 1 && files.First().EndsWith(".pdf")), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData("../etc/passwd")]
    [InlineData("../../secret.pdf")]
    [InlineData("2026-03-25/list.exe")]
    [InlineData("2026-03-25/sub/dir/list.pdf")]
    [InlineData("notadate/list.pdf")]
    [InlineData("")]
    public async Task Handle_InvalidBlobPath_ThrowsArgumentException(string blobPath)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _handler.Handle(new ReprintExpeditionListRequest { BlobPath = blobPath }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_InvalidBlobPath_NeverCallsPrintQueue()
    {
        try
        {
            await _handler.Handle(
                new ReprintExpeditionListRequest { BlobPath = "../traversal/list.pdf" },
                CancellationToken.None);
        }
        catch (ArgumentException) { }

        _printQueueSink.Verify(
            x => x.SendAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_AfterPrint_TempFileIsCleanedUp()
    {
        // Capture the temp file path sent to the printer
        IEnumerable<string>? capturedPaths = null;
        _printQueueSink
            .Setup(x => x.SendAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<string>, CancellationToken>((files, _) => capturedPaths = files)
            .Returns(Task.CompletedTask);

        await _handler.Handle(
            new ReprintExpeditionListRequest { BlobPath = "2026-03-25/list.pdf" },
            CancellationToken.None);

        Assert.NotNull(capturedPaths);
        // Temp file should be deleted after handler completes
        Assert.DoesNotContain(capturedPaths, File.Exists);
    }
}
