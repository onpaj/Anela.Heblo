using Anela.Heblo.Application.Features.ExpeditionList;
using Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.GetExpeditionDates;
using Anela.Heblo.Domain.Features.FileStorage;
using Microsoft.Extensions.Options;
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
        _handler = new GetExpeditionDatesHandler(_blobStorageServiceMock.Object, Options.Create(new PrintPickingListOptions()));
    }

    [Fact]
    public async Task Handle_ReturnsDatesSortedDescending()
    {
        // Arrange
        var prefixes = new List<string> { "2026-03-24", "2026-03-25", "2026-03-23" };
        _blobStorageServiceMock
            .Setup(s => s.ListVirtualDirectoriesAsync(ContainerName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(prefixes.AsReadOnly());

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
        var prefixes = new List<string>();
        for (int i = 1; i <= 25; i++)
        {
            prefixes.Add($"2026-01-{i:D2}");
        }

        _blobStorageServiceMock
            .Setup(s => s.ListVirtualDirectoriesAsync(ContainerName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(prefixes.AsReadOnly());

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
            .Setup(s => s.ListVirtualDirectoriesAsync(ContainerName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>().AsReadOnly());

        var request = new GetExpeditionDatesRequest { Page = 1, PageSize = 20 };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Dates);
    }

    [Fact]
    public async Task Handle_CallsListVirtualDirectoriesOnce_AndNeverCallsListBlobs()
    {
        // Arrange
        var prefixes = new List<string> { "2026-03-25", "2026-03-24" };
        _blobStorageServiceMock
            .Setup(s => s.ListVirtualDirectoriesAsync(ContainerName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(prefixes.AsReadOnly());

        var request = new GetExpeditionDatesRequest { Page = 1, PageSize = 20 };

        // Act
        await _handler.Handle(request, default);

        // Assert
        _blobStorageServiceMock.Verify(
            s => s.ListVirtualDirectoriesAsync(ContainerName, It.IsAny<CancellationToken>()),
            Times.Once);
        _blobStorageServiceMock.Verify(
            s => s.ListBlobsAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_FiltersOutNonDatePrefixes()
    {
        // Arrange — mix of valid dates, structurally wrong, semantically wrong, and a sentinel folder.
        var prefixes = new List<string>
        {
            "2026-03-25",       // valid
            "miscellaneous",    // not a date
            "2026-13-99",       // structurally yyyy-MM-dd but invalid month/day
            "2026-03-24",       // valid
            "not-a-date",       // not a date
            "2025-12-31"        // valid
        };
        _blobStorageServiceMock
            .Setup(s => s.ListVirtualDirectoriesAsync(ContainerName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(prefixes.AsReadOnly());

        var request = new GetExpeditionDatesRequest { Page = 1, PageSize = 20 };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert — only the three valid dates remain, in descending ordinal order.
        Assert.Equal(3, result.TotalCount);
        Assert.Equal(new[] { "2026-03-25", "2026-03-24", "2025-12-31" }, result.Dates);
    }
}
