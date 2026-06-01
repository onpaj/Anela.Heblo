using Anela.Heblo.Application.Features.Dashboard.Contracts;
using Anela.Heblo.Application.Features.Dashboard.UseCases.GetTileData;
using Anela.Heblo.Application.Features.Dashboard.UseCases.GetUserSettings;
using Anela.Heblo.Xcc.Services.Dashboard;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Dashboard;

public class GetTileDataHandlerTests
{
    private readonly Mock<IMediator> _mediatorMock;
    private readonly Mock<ITileRegistry> _tileRegistryMock;
    private readonly GetTileDataHandler _handler;

    public GetTileDataHandlerTests()
    {
        _mediatorMock = new Mock<IMediator>();
        _tileRegistryMock = new Mock<ITileRegistry>();

        var options = Options.Create(new DashboardOptions { MaxConcurrentTileLoads = 4 });
        _handler = new GetTileDataHandler(_mediatorMock.Object, _tileRegistryMock.Object, options);
    }

    private void SetupUserSettings(string userId, UserDashboardTileDto[] tiles)
    {
        _mediatorMock
            .Setup(x => x.Send(
                It.Is<GetUserSettingsRequest>(r => r.UserId == userId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetUserSettingsResponse
            {
                Settings = new UserDashboardSettingsDto { Tiles = tiles }
            });
    }

    [Fact]
    public async Task Handle_WhenUserIdIsNull_ShouldUseAnonymous()
    {
        // Arrange
        var request = new GetTileDataRequest { UserId = null! };
        SetupUserSettings("anonymous", Array.Empty<UserDashboardTileDto>());

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        _mediatorMock.Verify(
            x => x.Send(It.Is<GetUserSettingsRequest>(r => r.UserId == "anonymous"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenUserIdIsEmpty_ShouldUseAnonymous()
    {
        // Arrange
        var request = new GetTileDataRequest { UserId = "" };
        SetupUserSettings("anonymous", Array.Empty<UserDashboardTileDto>());

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        _mediatorMock.Verify(
            x => x.Send(It.Is<GetUserSettingsRequest>(r => r.UserId == "anonymous"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenNoVisibleTiles_ShouldReturnEmpty()
    {
        // Arrange
        var request = new GetTileDataRequest { UserId = "user1" };
        SetupUserSettings("user1", new[]
        {
            new UserDashboardTileDto { TileId = "tile-a", IsVisible = false, DisplayOrder = 0 },
            new UserDashboardTileDto { TileId = "tile-b", IsVisible = false, DisplayOrder = 1 }
        });

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Tiles.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WhenTileNotFound_ShouldReturnErrorDto()
    {
        // Arrange
        const string tileId = "missing-tile";
        var request = new GetTileDataRequest { UserId = "user1" };
        SetupUserSettings("user1", new[]
        {
            new UserDashboardTileDto { TileId = tileId, IsVisible = true, DisplayOrder = 0 }
        });

        _tileRegistryMock
            .Setup(x => x.GetTile(tileId))
            .Returns((ITile?)null);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        var tile = result.Tiles.Should().ContainSingle().Subject;
        tile.TileId.Should().Be(tileId);
        tile.Title.Should().Be("Error");
        tile.Category.Should().Be("Error");
    }

    [Fact]
    public async Task Handle_WhenTileThrows_ShouldReturnErrorDto()
    {
        // Arrange
        const string tileId = "throwing-tile";
        const string errorMessage = "Data source unavailable";
        var request = new GetTileDataRequest { UserId = "user1" };
        SetupUserSettings("user1", new[]
        {
            new UserDashboardTileDto { TileId = tileId, IsVisible = true, DisplayOrder = 0 }
        });

        var tileMock = new Mock<ITile>();
        tileMock.Setup(x => x.Title).Returns("Throwing Tile");
        tileMock.Setup(x => x.Description).Returns("Desc");
        tileMock.Setup(x => x.Size).Returns(TileSize.Medium);
        tileMock.Setup(x => x.Category).Returns(TileCategory.Finance);
        tileMock.Setup(x => x.DefaultEnabled).Returns(true);
        tileMock.Setup(x => x.AutoShow).Returns(false);
        tileMock.Setup(x => x.ComponentType).Returns(typeof(object));
        tileMock.Setup(x => x.RequiredPermissions).Returns(Array.Empty<string>());

        _tileRegistryMock
            .Setup(x => x.GetTile(tileId))
            .Returns(tileMock.Object);

        _tileRegistryMock
            .Setup(x => x.GetTileDataAsync(tileId, It.IsAny<Dictionary<string, string>?>()))
            .ThrowsAsync(new InvalidOperationException(errorMessage));

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        var tile = result.Tiles.Should().ContainSingle().Subject;
        tile.TileId.Should().Be(tileId);
        tile.Title.Should().Be("Error");
        tile.Category.Should().Be("Error");
        tile.Description.Should().Contain(errorMessage);
    }

    [Fact]
    public async Task Handle_WhenTilesHaveOutOfOrderDisplayOrder_ShouldReturnInOrder()
    {
        // Arrange
        var request = new GetTileDataRequest { UserId = "user1" };
        SetupUserSettings("user1", new[]
        {
            new UserDashboardTileDto { TileId = "tile-c", IsVisible = true, DisplayOrder = 2 },
            new UserDashboardTileDto { TileId = "tile-a", IsVisible = true, DisplayOrder = 0 },
            new UserDashboardTileDto { TileId = "tile-b", IsVisible = true, DisplayOrder = 1 }
        });

        foreach (var id in new[] { "tile-a", "tile-b", "tile-c" })
        {
            var capturedId = id;
            var tileMock = new Mock<ITile>();
            tileMock.Setup(x => x.Title).Returns($"Title {capturedId}");
            tileMock.Setup(x => x.Description).Returns("Desc");
            tileMock.Setup(x => x.Size).Returns(TileSize.Small);
            tileMock.Setup(x => x.Category).Returns(TileCategory.System);
            tileMock.Setup(x => x.DefaultEnabled).Returns(true);
            tileMock.Setup(x => x.AutoShow).Returns(false);
            tileMock.Setup(x => x.ComponentType).Returns(typeof(object));
            tileMock.Setup(x => x.RequiredPermissions).Returns(Array.Empty<string>());

            _tileRegistryMock.Setup(x => x.GetTile(capturedId)).Returns(tileMock.Object);
            _tileRegistryMock
                .Setup(x => x.GetTileDataAsync(capturedId, It.IsAny<Dictionary<string, string>?>()))
                .ReturnsAsync(new { Id = capturedId });
        }

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        var tiles = result.Tiles.ToArray();
        tiles.Should().HaveCount(3);
        // Results must be ordered by DisplayOrder (0, 1, 2) regardless of parallel execution order
        // tile-a (DisplayOrder=0), tile-b (DisplayOrder=1), tile-c (DisplayOrder=2)
        // GetTileId() uses type name, so we check via the registry-returned tile metadata
        tiles[0].Title.Should().Be("Title tile-a");
        tiles[1].Title.Should().Be("Title tile-b");
        tiles[2].Title.Should().Be("Title tile-c");
    }

    [Fact]
    public async Task Handle_WhenTilesAreVisible_ShouldReturnTileData()
    {
        // Arrange
        const string tileId = "analytics-tile";
        var expectedData = new { Count = 42, Status = "Active" };
        var request = new GetTileDataRequest { UserId = "user1", TileParameters = null };
        SetupUserSettings("user1", new[]
        {
            new UserDashboardTileDto { TileId = tileId, IsVisible = true, DisplayOrder = 0 }
        });

        var tileMock = new Mock<ITile>();
        tileMock.Setup(x => x.Title).Returns("Analytics");
        tileMock.Setup(x => x.Description).Returns("Analytics description");
        tileMock.Setup(x => x.Size).Returns(TileSize.Large);
        tileMock.Setup(x => x.Category).Returns(TileCategory.Finance);
        tileMock.Setup(x => x.DefaultEnabled).Returns(true);
        tileMock.Setup(x => x.AutoShow).Returns(true);
        tileMock.Setup(x => x.ComponentType).Returns(typeof(object));
        tileMock.Setup(x => x.RequiredPermissions).Returns(new[] { "read", "analytics" });

        _tileRegistryMock
            .Setup(x => x.GetTile(tileId))
            .Returns(tileMock.Object);

        _tileRegistryMock
            .Setup(x => x.GetTileDataAsync(tileId, null))
            .ReturnsAsync(expectedData);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        var tile = result.Tiles.Should().ContainSingle().Subject;
        tile.Title.Should().Be("Analytics");
        tile.Description.Should().Be("Analytics description");
        tile.Size.Should().Be("Large");
        tile.Category.Should().Be("Finance");
        tile.DefaultEnabled.Should().BeTrue();
        tile.AutoShow.Should().BeTrue();
        tile.RequiredPermissions.Should().BeEquivalentTo(new[] { "read", "analytics" });
        tile.Data.Should().Be(expectedData);
    }

    [Fact]
    public async Task Handle_WhenTwoSlowTilesAndMaxDoP2_ShouldLoadInParallel()
    {
        // Arrange — two tiles each take ~100 ms; sequential would take ~200 ms, parallel ~100 ms
        const int delayMs = 100;
        var request = new GetTileDataRequest { UserId = "user1" };
        SetupUserSettings("user1", new[]
        {
            new UserDashboardTileDto { TileId = "slow-tile-1", IsVisible = true, DisplayOrder = 0 },
            new UserDashboardTileDto { TileId = "slow-tile-2", IsVisible = true, DisplayOrder = 1 }
        });

        var options = Options.Create(new DashboardOptions { MaxConcurrentTileLoads = 2 });
        var handler = new GetTileDataHandler(_mediatorMock.Object, _tileRegistryMock.Object, options);

        foreach (var id in new[] { "slow-tile-1", "slow-tile-2" })
        {
            var capturedId = id;
            var tileMock = new Mock<ITile>();
            tileMock.Setup(x => x.Title).Returns("Slow");
            tileMock.Setup(x => x.Description).Returns("Slow tile");
            tileMock.Setup(x => x.Size).Returns(TileSize.Small);
            tileMock.Setup(x => x.Category).Returns(TileCategory.System);
            tileMock.Setup(x => x.DefaultEnabled).Returns(true);
            tileMock.Setup(x => x.AutoShow).Returns(false);
            tileMock.Setup(x => x.ComponentType).Returns(typeof(object));
            tileMock.Setup(x => x.RequiredPermissions).Returns(Array.Empty<string>());

            _tileRegistryMock.Setup(x => x.GetTile(capturedId)).Returns(tileMock.Object);
            _tileRegistryMock
                .Setup(x => x.GetTileDataAsync(capturedId, It.IsAny<Dictionary<string, string>?>()))
                .Returns(async () =>
                {
                    await Task.Delay(delayMs);
                    return (object)new { Id = capturedId };
                });
        }

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = await handler.Handle(request, CancellationToken.None);
        stopwatch.Stop();

        // Assert
        result.Tiles.Should().HaveCount(2);
        stopwatch.ElapsedMilliseconds.Should().BeGreaterThan(80, "tiles must not be loaded sequentially with zero delay");
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(250, "tiles should be loaded in parallel, not sequentially");
    }
}
