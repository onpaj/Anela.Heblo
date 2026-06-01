using Anela.Heblo.Application.Features.Dashboard.UseCases.GetTileData;
using Anela.Heblo.Xcc.Services.Dashboard;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Dashboard;

public class GetTileDataHandlerTests
{
    private readonly Mock<IDashboardService> _dashboardServiceMock;
    private readonly GetTileDataHandler _handler;

    public GetTileDataHandlerTests()
    {
        _dashboardServiceMock = new Mock<IDashboardService>();
        _handler = new GetTileDataHandler(_dashboardServiceMock.Object);
    }

    [Fact]
    public async Task Handle_WhenUserIdIsNull_ShouldUseAnonymous()
    {
        // Arrange
        var request = new GetTileDataRequest { UserId = null };
        var tileData = CreateSampleTileData();

        _dashboardServiceMock
            .Setup(x => x.GetTileDataAsync("anonymous", It.IsAny<Dictionary<string, string>?>()))
            .ReturnsAsync(tileData);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Tiles.Should().NotBeNull();
        _dashboardServiceMock.Verify(x => x.GetTileDataAsync("anonymous", It.IsAny<Dictionary<string, string>?>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenUserIdIsEmpty_ShouldUseAnonymous()
    {
        // Arrange
        var request = new GetTileDataRequest { UserId = "" };
        var tileData = CreateSampleTileData();

        _dashboardServiceMock
            .Setup(x => x.GetTileDataAsync("anonymous", It.IsAny<Dictionary<string, string>?>()))
            .ReturnsAsync(tileData);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Tiles.Should().NotBeNull();
        _dashboardServiceMock.Verify(x => x.GetTileDataAsync("anonymous", It.IsAny<Dictionary<string, string>?>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenValidUserId_ShouldReturnTileData()
    {
        // Arrange
        var userId = "user123";
        var request = new GetTileDataRequest { UserId = userId };
        var tileData = CreateSampleTileData();

        _dashboardServiceMock
            .Setup(x => x.GetTileDataAsync(userId, It.IsAny<Dictionary<string, string>?>()))
            .ReturnsAsync(tileData);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Tiles.Should().NotBeNull();
        result.Tiles.Should().HaveCount(2);

        var tiles = result.Tiles.ToArray();
        tiles[0].TileId.Should().Be("analytics-tile");
        tiles[0].Title.Should().Be("Analytics");
        tiles[0].Description.Should().Be("System analytics data");
        tiles[0].Size.Should().Be("Large");
        tiles[0].Category.Should().Be("Finance");
        tiles[0].Data.Should().NotBeNull();

        tiles[1].TileId.Should().Be("finance-tile");
        tiles[1].Title.Should().Be("Finance");
        tiles[1].Description.Should().Be("Financial overview");
        tiles[1].Size.Should().Be("Medium");
        tiles[1].Category.Should().Be("Finance");
        tiles[1].Data.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_WhenNoTileData_ShouldReturnEmptyList()
    {
        // Arrange
        var userId = "user123";
        var request = new GetTileDataRequest { UserId = userId };

        _dashboardServiceMock
            .Setup(x => x.GetTileDataAsync(userId, It.IsAny<Dictionary<string, string>?>()))
            .ReturnsAsync(new List<TileData>());

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Tiles.Should().NotBeNull();
        result.Tiles.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WhenTileDataWithNullData_ShouldReturnTileWithNullData()
    {
        // Arrange
        var userId = "user123";
        var request = new GetTileDataRequest { UserId = userId };
        var tileData = new List<TileData>
        {
            new()
            {
                TileId = "empty-tile",
                Title = "Empty Tile",
                Description = "Tile with no data",
                Size = TileSize.Small,
                Category = TileCategory.Orders,
                DefaultEnabled = true,
                AutoShow = false,
                ComponentType = typeof(object),
                RequiredPermissions = Array.Empty<string>(),
                Data = null
            }
        };

        _dashboardServiceMock
            .Setup(x => x.GetTileDataAsync(userId, It.IsAny<Dictionary<string, string>?>()))
            .ReturnsAsync(tileData);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Tiles.Should().HaveCount(1);
        var tile = result.Tiles.First();
        tile.TileId.Should().Be("empty-tile");
        tile.Data.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ShouldCallDashboardServiceOnce()
    {
        // Arrange
        var userId = "user123";
        var request = new GetTileDataRequest { UserId = userId };
        var tileData = CreateSampleTileData();

        _dashboardServiceMock
            .Setup(x => x.GetTileDataAsync(userId, It.IsAny<Dictionary<string, string>?>()))
            .ReturnsAsync(tileData);

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        _dashboardServiceMock.Verify(x => x.GetTileDataAsync(userId, It.IsAny<Dictionary<string, string>?>()), Times.Once);
    }

    private static List<TileData> CreateSampleTileData()
    {
        return new List<TileData>
        {
            new()
            {
                TileId = "analytics-tile",
                Title = "Analytics",
                Description = "System analytics data",
                Size = TileSize.Large,
                Category = TileCategory.Finance,
                DefaultEnabled = true,
                AutoShow = true,
                ComponentType = typeof(object),
                RequiredPermissions = new[] { "read", "analytics" },
                Data = new { Count = 42, Status = "Active" }
            },
            new()
            {
                TileId = "finance-tile",
                Title = "Finance",
                Description = "Financial overview",
                Size = TileSize.Medium,
                Category = TileCategory.Finance,
                DefaultEnabled = false,
                AutoShow = false,
                ComponentType = typeof(object),
                RequiredPermissions = new[] { "finance", "read" },
                Data = new { Revenue = 15000.50m, Expenses = 8500.25m }
            }
        };
    }
}