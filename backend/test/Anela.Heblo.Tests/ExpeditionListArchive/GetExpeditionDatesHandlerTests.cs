using Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.GetExpeditionDates;
using Anela.Heblo.Domain.Features.FileStorage;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.ExpeditionListArchive;

public class GetExpeditionDatesHandlerTests
{
    private readonly Mock<IBlobStorageService> _blobStorageServiceMock;
    private readonly GetExpeditionDatesHandler _handler;
    private const string ContainerName = "expedition-lists";

    public GetExpeditionDatesHandlerTests()
    {
        _blobStorageServiceMock = new Mock<IBlobStorageService>();
        _handler = new GetExpeditionDatesHandler(_blobStorageServiceMock.Object);
    }

    [Fact]
    public async Task Handle_ReturnsDatesSortedDescending()
    {
        // Arrange
        var blobs = new List<BlobItemInfo>
        {
            new() { Name = "2026-03-24/list-001.pdf", FileName = "list-001.pdf", CreatedOn = DateTimeOffset.UtcNow.AddDays(-1) },
            new() { Name = "2026-03-25/list-002.pdf", FileName = "list-002.pdf", CreatedOn = DateTimeOffset.UtcNow },
            new() { Name = "2026-03-24/list-003.pdf", FileName = "list-003.pdf", CreatedOn = DateTimeOffset.UtcNow.AddDays(-1) },
            new() { Name = "2026-03-23/list-004.pdf", FileName = "list-004.pdf", CreatedOn = DateTimeOffset.UtcNow.AddDays(-2) },
        };

        _blobStorageServiceMock
            .Setup(s => s.ListBlobsAsync(ContainerName, null, default))
            .ReturnsAsync(blobs.AsReadOnly());

        var request = new GetExpeditionDatesRequest { Page = 1, PageSize = 20 };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.TotalCount);
        Assert.Equal(3, result.Dates.Count);
        Assert.Equal("2026-03-25", result.Dates[0]);
        Assert.Equal("2026-03-24", result.Dates[1]);
        Assert.Equal("2026-03-23", result.Dates[2]);
    }

    [Fact]
    public async Task Handle_PaginatesCorrectly()
    {
        // Arrange
        var blobs = new List<BlobItemInfo>();
        for (int i = 1; i <= 25; i++)
        {
            var dateStr = $"2026-01-{i:D2}";
            blobs.Add(new() { Name = $"{dateStr}/list.pdf", FileName = "list.pdf" });
        }

        _blobStorageServiceMock
            .Setup(s => s.ListBlobsAsync(ContainerName, null, default))
            .ReturnsAsync(blobs.AsReadOnly());

        var request = new GetExpeditionDatesRequest { Page = 2, PageSize = 20 };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.Equal(25, result.TotalCount);
        Assert.Equal(5, result.Dates.Count); // page 2 of 20: items 21-25
    }

    [Fact]
    public async Task Handle_EmptyContainer_ReturnsEmptyList()
    {
        // Arrange
        _blobStorageServiceMock
            .Setup(s => s.ListBlobsAsync(ContainerName, null, default))
            .ReturnsAsync(new List<BlobItemInfo>().AsReadOnly());

        var request = new GetExpeditionDatesRequest { Page = 1, PageSize = 20 };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Dates);
    }
}
