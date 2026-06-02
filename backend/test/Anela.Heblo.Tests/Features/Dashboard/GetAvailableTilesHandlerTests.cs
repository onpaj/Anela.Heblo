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
            .Returns(new List<TileMetadata>());

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

        var tiles = new List<TileMetadata>
        {
            new TileMetadata("test1", "Test Tile 1", "Description 1", TileSize.Small, TileCategory.Finance, true, false, typeof(object), new[] { "read" }),
            new TileMetadata("test2", "Test Tile 2", "Description 2", TileSize.Large, TileCategory.Finance, false, true, typeof(object), new[] { "admin", "write" })
        };

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

        resultTiles[0].TileId.Should().Be("test1");
        resultTiles[0].Title.Should().Be("Test Tile 1");
        resultTiles[0].Description.Should().Be("Description 1");
        resultTiles[0].Size.Should().Be("Small");
        resultTiles[0].Category.Should().Be("Finance");
        resultTiles[0].DefaultEnabled.Should().BeTrue();
        resultTiles[0].AutoShow.Should().BeFalse();
        resultTiles[0].RequiredPermissions.Should().Equal(new[] { "read" });

        resultTiles[1].TileId.Should().Be("test2");
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

        var tiles = new List<TileMetadata>
        {
            new TileMetadata("testtilenoermissions", "Test Tile", "Description", TileSize.Medium, TileCategory.System, true, false, typeof(object), Array.Empty<string>())
        };

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
            .Returns(new List<TileMetadata>());

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        _tileRegistryMock.Verify(x => x.GetAvailableTiles(), Times.Once);
    }
}
