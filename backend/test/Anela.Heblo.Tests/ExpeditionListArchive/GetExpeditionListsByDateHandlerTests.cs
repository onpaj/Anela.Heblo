using Anela.Heblo.Application.Features.ExpeditionList;
using Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.GetExpeditionListsByDate;
using Anela.Heblo.Domain.Features.FileStorage;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.ExpeditionListArchive;

public class GetExpeditionListsByDateHandlerTests
{
    private readonly Mock<IBlobStorageService> _blobStorageServiceMock;
    private readonly GetExpeditionListsByDateHandler _handler;
    private const string ContainerName = "expedition-lists";

    public GetExpeditionListsByDateHandlerTests()
    {
        _blobStorageServiceMock = new Mock<IBlobStorageService>();
        _handler = new GetExpeditionListsByDateHandler(_blobStorageServiceMock.Object, Options.Create(new PrintPickingListOptions()));
    }

    [Fact]
    public async Task Handle_ReturnsItemsForDate()
    {
        // Arrange
        var date = "2026-03-25";
        var blobs = new List<BlobItemInfo>
        {
            new() { Name = $"{date}/picking-list-001.pdf", FileName = "picking-list-001.pdf", CreatedOn = new DateTimeOffset(2026, 3, 25, 10, 0, 0, TimeSpan.Zero), ContentLength = 512000 },
            new() { Name = $"{date}/picking-list-002.pdf", FileName = "picking-list-002.pdf", CreatedOn = new DateTimeOffset(2026, 3, 25, 14, 0, 0, TimeSpan.Zero), ContentLength = 256000 },
        };

        _blobStorageServiceMock
            .Setup(s => s.ListBlobsAsync(ContainerName, date, default))
            .ReturnsAsync(blobs.AsReadOnly());

        var request = new GetExpeditionListsByDateRequest { Date = date };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal("picking-list-001.pdf", result.Items[0].FileName);
        Assert.Equal($"{date}/picking-list-001.pdf", result.Items[0].BlobPath);
        Assert.Equal(512000, result.Items[0].ContentLength);
    }

    [Fact]
    public async Task Handle_FiltersPdfFilesOnly()
    {
        // Arrange
        var date = "2026-03-25";
        var blobs = new List<BlobItemInfo>
        {
            new() { Name = $"{date}/picking-list-001.pdf", FileName = "picking-list-001.pdf" },
            new() { Name = $"{date}/picking-list-002.txt", FileName = "picking-list-002.txt" },
        };

        _blobStorageServiceMock
            .Setup(s => s.ListBlobsAsync(ContainerName, date, default))
            .ReturnsAsync(blobs.AsReadOnly());

        var request = new GetExpeditionListsByDateRequest { Date = date };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.Single(result.Items);
        Assert.Equal("picking-list-001.pdf", result.Items[0].FileName);
    }
}
