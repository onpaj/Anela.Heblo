using Anela.Heblo.Application.Features.Dashboard.Contracts;
using Anela.Heblo.Application.Features.Dashboard.UseCases.GetTileData;
using Anela.Heblo.Application.Features.Dashboard.UseCases.GetUserSettings;
using Anela.Heblo.Xcc.Services.Dashboard;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Dashboard;

public class GetTileDataHandlerTests
{
    private readonly Mock<IMediator> _mediatorMock;
    private readonly Mock<ITileRegistry> _tileRegistryMock;
    private readonly Mock<ILogger<GetTileDataHandler>> _loggerMock;
    private readonly GetTileDataHandler _handler;

    public GetTileDataHandlerTests()
    {
        _mediatorMock = new Mock<IMediator>();
        _tileRegistryMock = new Mock<ITileRegistry>();
        _loggerMock = new Mock<ILogger<GetTileDataHandler>>();

        var options = Options.Create(new DashboardOptions { MaxConcurrentTileLoads = 4 });
        _handler = new GetTileDataHandler(_mediatorMock.Object, _tileRegistryMock.Object, options, _loggerMock.Object);
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
            .Setup(x => x.GetTileMetadata(tileId))
            .Returns((TileMetadata?)null);

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

        _tileRegistryMock
            .Setup(x => x.GetTileMetadata(tileId))
            .Returns(new TileMetadata(tileId, "Throwing Tile", "Desc", TileSize.Medium,
                TileCategory.Finance, true, false, Array.Empty<string>()));

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
        tile.Description.Should().Be($"Failed to load tile '{tileId}'");
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
            _tileRegistryMock.Setup(x => x.GetTileMetadata(capturedId))
                .Returns(new TileMetadata(capturedId, $"Title {capturedId}", "Desc", TileSize.Small,
                    TileCategory.System, true, false, Array.Empty<string>()));
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

        _tileRegistryMock
            .Setup(x => x.GetTileMetadata(tileId))
            .Returns(new TileMetadata(tileId, "Analytics", "Analytics description", TileSize.Large,
                TileCategory.Finance, true, true, new[] { "read", "analytics" }));

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
        // Arrange — verify structural parallelism: both tiles must be in-flight simultaneously.
        // Each tile waits until both have started before completing. With MaxDegreeOfParallelism=2,
        // Parallel.ForEachAsync dispatches both lambdas before either finishes, so both tiles start
        // before either awaits its result — the TCS resolves immediately and both tiles complete.
        // With sequential execution (MaxDegreeOfParallelism=1), tile 1 would wait indefinitely for
        // tile 2 to start, which never happens, and the 10-second timeout fires instead.
        // This avoids wall-clock timing assertions that are flaky on loaded CI runners.
        const int tileCount = 2;
        var startedCount = 0;
        var allStartedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var anyTimedOut = false;

        var request = new GetTileDataRequest { UserId = "user1" };
        SetupUserSettings("user1", new[]
        {
            new UserDashboardTileDto { TileId = "slow-tile-1", IsVisible = true, DisplayOrder = 0 },
            new UserDashboardTileDto { TileId = "slow-tile-2", IsVisible = true, DisplayOrder = 1 }
        });

        var options = Options.Create(new DashboardOptions { MaxConcurrentTileLoads = 2 });
        var handler = new GetTileDataHandler(_mediatorMock.Object, _tileRegistryMock.Object, options, _loggerMock.Object);

        foreach (var id in new[] { "slow-tile-1", "slow-tile-2" })
        {
            var capturedId = id;
            _tileRegistryMock.Setup(x => x.GetTileMetadata(capturedId))
                .Returns(new TileMetadata(capturedId, "Slow", "Slow tile", TileSize.Small,
                    TileCategory.System, true, false, Array.Empty<string>()));
            _tileRegistryMock
                .Setup(x => x.GetTileDataAsync(capturedId, It.IsAny<Dictionary<string, string>?>()))
                .Returns(async () =>
                {
                    if (Interlocked.Increment(ref startedCount) >= tileCount)
                        allStartedTcs.TrySetResult(true);

                    try
                    {
                        await allStartedTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    }
                    catch (TimeoutException)
                    {
                        anyTimedOut = true;
                        throw;
                    }

                    return (object)new { Id = capturedId };
                });
        }

        // Act
        var result = await handler.Handle(request, CancellationToken.None);

        // Assert
        anyTimedOut.Should().BeFalse("tiles should be loaded in parallel, not sequentially");
        result.Tiles.Should().HaveCount(tileCount);
    }
}
