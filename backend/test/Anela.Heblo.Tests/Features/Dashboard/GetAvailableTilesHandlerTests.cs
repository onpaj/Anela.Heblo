using Anela.Heblo.Application.Features.Dashboard.Contracts;
using Anela.Heblo.Application.Features.Dashboard.UseCases.GetAvailableTiles;
using Anela.Heblo.Xcc.Services.Dashboard;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Dashboard;

public class GetAvailableTilesHandlerTests
{
    private readonly Mock<ITileRegistry> _tileRegistryMock;
    private readonly GetAvailableTilesHandler _handler;

    public GetAvailableTilesHandlerTests()
    {
        _tileRegistryMock = new Mock<ITileRegistry>();
        _handler = new GetAvailableTilesHandler(_tileRegistryMock.Object);
    }

    [Fact]
    public async Task Handle_WhenNoTilesAvailable_ShouldReturnEmptyList()
    {
        // Arrange
        var request = new GetAvailableTilesRequest();
        _tileRegistryMock
            .Setup(x => x.GetAvailableTiles())
            .Returns(new List<ITile>());

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Tiles.Should().NotBeNull();
        result.Tiles.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WhenTilesAvailable_ShouldReturnMappedTiles()
    {
        // Arrange
        var request = new GetAvailableTilesRequest();
        var tile1 = new TestTile1();
        var tile2 = new TestTile2();

        var tiles = new List<ITile> { tile1, tile2 };
        _tileRegistryMock
            .Setup(x => x.GetAvailableTiles())
            .Returns(tiles);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Tiles.Should().NotBeNull();
        result.Tiles.Should().HaveCount(2);

        var resultTiles = result.Tiles.ToArray();

        resultTiles[0].TileId.Should().Be("test1"); // TestTile1 -> test1
        resultTiles[0].Title.Should().Be("Test Tile 1");
        resultTiles[0].Description.Should().Be("Description 1");
        resultTiles[0].Size.Should().Be("Small");
        resultTiles[0].Category.Should().Be("Finance");
        resultTiles[0].DefaultEnabled.Should().BeTrue();
        resultTiles[0].AutoShow.Should().BeFalse();
        resultTiles[0].RequiredPermissions.Should().Equal(new[] { "read" });

        resultTiles[1].TileId.Should().Be("test2"); // TestTile2 -> test2
        resultTiles[1].Title.Should().Be("Test Tile 2");
        resultTiles[1].Description.Should().Be("Description 2");
        resultTiles[1].Size.Should().Be("Large");
        resultTiles[1].Category.Should().Be("Finance");
        resultTiles[1].DefaultEnabled.Should().BeFalse();
        resultTiles[1].AutoShow.Should().BeTrue();
        resultTiles[1].RequiredPermissions.Should().Equal(new[] { "admin", "write" });
    }

    [Fact]
    public async Task Handle_WhenTileHasNoPermissions_ShouldReturnEmptyPermissionsArray()
    {
        // Arrange
        var request = new GetAvailableTilesRequest();
        var tile = new TestTileNoPermissions();

        var tiles = new List<ITile> { tile };
        _tileRegistryMock
            .Setup(x => x.GetAvailableTiles())
            .Returns(tiles);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Tiles.Should().HaveCount(1);
        var resultTile = result.Tiles.First();
        resultTile.RequiredPermissions.Should().NotBeNull();
        resultTile.RequiredPermissions.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ShouldCallTileRegistryOnce()
    {
        // Arrange
        var request = new GetAvailableTilesRequest();
        _tileRegistryMock
            .Setup(x => x.GetAvailableTiles())
            .Returns(new List<ITile>());

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        _tileRegistryMock.Verify(x => x.GetAvailableTiles(), Times.Once);
    }
}

// Test tile classes for testing
public class TestTile1 : ITile
{
    public string Title { get; init; } = "Test Tile 1";
    public string Description { get; init; } = "Description 1";
    public TileSize Size { get; init; } = TileSize.Small;
    public TileCategory Category { get; init; } = TileCategory.Finance;
    public bool DefaultEnabled { get; init; } = true;
    public bool AutoShow { get; init; } = false;
    public Type ComponentType { get; init; } = typeof(object);
    public string[] RequiredPermissions { get; init; } = new[] { "read" };

    public Task<object> LoadDataAsync(Dictionary<string, string>? parameters = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult((object)"Test Data");
    }
}

public class TestTile2 : ITile
{
    public string Title { get; init; } = "Test Tile 2";
    public string Description { get; init; } = "Description 2";
    public TileSize Size { get; init; } = TileSize.Large;
    public TileCategory Category { get; init; } = TileCategory.Finance;
    public bool DefaultEnabled { get; init; } = false;
    public bool AutoShow { get; init; } = true;
    public Type ComponentType { get; init; } = typeof(object);
    public string[] RequiredPermissions { get; init; } = new[] { "admin", "write" };

    public Task<object> LoadDataAsync(Dictionary<string, string>? parameters = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult((object)"Test Data 2");
    }
}

public class TestTileNoPermissions : ITile
{
    public string Title { get; init; } = "Test Tile";
    public string Description { get; init; } = "Description";
    public TileSize Size { get; init; } = TileSize.Medium;
    public TileCategory Category { get; init; } = TileCategory.System;
    public bool DefaultEnabled { get; init; } = true;
    public bool AutoShow { get; init; } = false;
    public Type ComponentType { get; init; } = typeof(object);
    public string[] RequiredPermissions { get; init; } = Array.Empty<string>();

    public Task<object> LoadDataAsync(Dictionary<string, string>? parameters = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult((object)"Test Data");
    }
}