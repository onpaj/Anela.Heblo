using Anela.Heblo.Application.Features.Dashboard.Infrastructure;
using Anela.Heblo.Application.Features.Dashboard.UseCases.GetUserSettings;
using Anela.Heblo.Xcc.Domain;
using Anela.Heblo.Xcc.Services.Dashboard;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Dashboard;

public class GetUserSettingsHandlerTests
{
    private readonly Mock<ITileRegistry> _tileRegistryMock;
    private readonly Mock<IUserDashboardSettingsRepository> _repositoryMock;
    private readonly Mock<IUserDashboardSettingsLock> _lockMock;
    private readonly TimeProvider _timeProvider;
    private readonly GetUserSettingsHandler _handler;

    private static readonly DateTime FixedUtcNow = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    public GetUserSettingsHandlerTests()
    {
        _tileRegistryMock = new Mock<ITileRegistry>();
        _repositoryMock = new Mock<IUserDashboardSettingsRepository>();
        _lockMock = new Mock<IUserDashboardSettingsLock>();

        var timeProviderMock = new Mock<TimeProvider>();
        timeProviderMock
            .Setup(x => x.GetUtcNow())
            .Returns(new DateTimeOffset(FixedUtcNow));
        _timeProvider = timeProviderMock.Object;

        // No-op disposable returned by the lock
        var noOpDisposable = new Mock<IAsyncDisposable>();
        noOpDisposable
            .Setup(x => x.DisposeAsync())
            .Returns(ValueTask.CompletedTask);
        _lockMock
            .Setup(x => x.AcquireAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(noOpDisposable.Object);

        _handler = new GetUserSettingsHandler(
            _tileRegistryMock.Object,
            _repositoryMock.Object,
            _lockMock.Object,
            _timeProvider);
    }

    [Fact]
    public async Task Handle_WhenUserIdIsNull_ShouldUseAnonymous()
    {
        // Arrange
        var request = new GetUserSettingsRequest { UserId = null };
        _tileRegistryMock.Setup(x => x.GetAvailableTiles()).Returns(new List<TileMetadata>());
        _repositoryMock
            .Setup(x => x.GetByUserIdAsync("anonymous"))
            .ReturnsAsync((UserDashboardSettings?)null);
        _repositoryMock
            .Setup(x => x.AddAsync(It.IsAny<UserDashboardSettings>()))
            .ReturnsAsync((UserDashboardSettings s) => s);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Settings.Should().NotBeNull();
        _repositoryMock.Verify(x => x.GetByUserIdAsync("anonymous"), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenUserIdIsEmpty_ShouldUseAnonymous()
    {
        // Arrange
        var request = new GetUserSettingsRequest { UserId = "" };
        _tileRegistryMock.Setup(x => x.GetAvailableTiles()).Returns(new List<TileMetadata>());
        _repositoryMock
            .Setup(x => x.GetByUserIdAsync("anonymous"))
            .ReturnsAsync((UserDashboardSettings?)null);
        _repositoryMock
            .Setup(x => x.AddAsync(It.IsAny<UserDashboardSettings>()))
            .ReturnsAsync((UserDashboardSettings s) => s);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Settings.Should().NotBeNull();
        _repositoryMock.Verify(x => x.GetByUserIdAsync("anonymous"), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenNewUser_ShouldCreateDefaultSettingsWithAutoShowTiles()
    {
        // Arrange
        var userId = "user123";
        var request = new GetUserSettingsRequest { UserId = userId };
        var autoTiles = new List<TileMetadata>
        {
            MakeTile("auto1", defaultEnabled: true, autoShow: true),
            MakeTile("auto2", defaultEnabled: true, autoShow: true)
        };

        _tileRegistryMock.Setup(x => x.GetAvailableTiles()).Returns(autoTiles);
        _repositoryMock
            .Setup(x => x.GetByUserIdAsync(userId))
            .ReturnsAsync((UserDashboardSettings?)null);
        UserDashboardSettings? capturedSettings = null;
        _repositoryMock
            .Setup(x => x.AddAsync(It.IsAny<UserDashboardSettings>()))
            .Callback<UserDashboardSettings>(s => capturedSettings = s)
            .ReturnsAsync((UserDashboardSettings s) => s);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Settings.Tiles.Should().HaveCount(2);

        var tiles = result.Settings.Tiles.OrderBy(t => t.DisplayOrder).ToArray();
        tiles[0].TileId.Should().Be("auto1");
        tiles[0].IsVisible.Should().BeTrue();
        tiles[0].DisplayOrder.Should().Be(0);
        tiles[1].TileId.Should().Be("auto2");
        tiles[1].IsVisible.Should().BeTrue();
        tiles[1].DisplayOrder.Should().Be(1);

        capturedSettings.Should().NotBeNull();
        capturedSettings!.LastModified.Should().Be(FixedUtcNow);
        capturedSettings.Tiles.Should().AllSatisfy(t => t.LastModified.Should().Be(FixedUtcNow));

        _repositoryMock.Verify(x => x.AddAsync(It.Is<UserDashboardSettings>(s =>
            s.UserId == userId &&
            s.Tiles.Count == 2)), Times.Once);
        _repositoryMock.Verify(x => x.UpdateAsync(It.IsAny<UserDashboardSettings>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenNewUser_WithNoAutoShowTiles_ShouldCreateEmptySettings()
    {
        // Arrange
        var userId = "user123";
        var request = new GetUserSettingsRequest { UserId = userId };
        var tiles = new List<TileMetadata> { MakeTile("manual", defaultEnabled: true, autoShow: false) };

        _tileRegistryMock.Setup(x => x.GetAvailableTiles()).Returns(tiles);
        _repositoryMock
            .Setup(x => x.GetByUserIdAsync(userId))
            .ReturnsAsync((UserDashboardSettings?)null);
        _repositoryMock
            .Setup(x => x.AddAsync(It.IsAny<UserDashboardSettings>()))
            .ReturnsAsync((UserDashboardSettings s) => s);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Settings.Tiles.Should().BeEmpty();

        _repositoryMock.Verify(x => x.AddAsync(It.Is<UserDashboardSettings>(s =>
            s.UserId == userId &&
            s.Tiles.Count == 0)), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenExistingUser_NoNewAutoShowTiles_ShouldNotCallUpdate()
    {
        // Arrange
        var userId = "user123";
        var request = new GetUserSettingsRequest { UserId = userId };
        var existingSettings = CreateExistingUserSettings(userId);

        // Registry returns only the tiles already in existing settings
        var allTiles = new List<TileMetadata>
        {
            MakeTile("auto1", defaultEnabled: true, autoShow: true),
            MakeTile("auto2", defaultEnabled: true, autoShow: true)
        };
        _tileRegistryMock.Setup(x => x.GetAvailableTiles()).Returns(allTiles);
        _repositoryMock
            .Setup(x => x.GetByUserIdAsync(userId))
            .ReturnsAsync(existingSettings);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Settings.Tiles.Should().HaveCount(2);

        _repositoryMock.Verify(x => x.AddAsync(It.IsAny<UserDashboardSettings>()), Times.Never);
        _repositoryMock.Verify(x => x.UpdateAsync(It.IsAny<UserDashboardSettings>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenExistingUser_WithNewAutoShowTile_ShouldBackfillAndUpdate()
    {
        // Arrange
        var userId = "user123";
        var request = new GetUserSettingsRequest { UserId = userId };
        var existingSettings = CreateExistingUserSettings(userId); // has auto1 (order 0) and auto2 (order 1)

        // Registry has an additional new AutoShow tile
        var allTiles = new List<TileMetadata>
        {
            MakeTile("auto1", defaultEnabled: true, autoShow: true),
            MakeTile("auto2", defaultEnabled: true, autoShow: true),
            MakeTile("newautoshow", defaultEnabled: true, autoShow: true)
        };
        _tileRegistryMock.Setup(x => x.GetAvailableTiles()).Returns(allTiles);
        _repositoryMock
            .Setup(x => x.GetByUserIdAsync(userId))
            .ReturnsAsync(existingSettings);

        UserDashboardSettings? capturedUpdate = null;
        _repositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<UserDashboardSettings>()))
            .Callback<UserDashboardSettings>(s => capturedUpdate = s);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Settings.Tiles.Should().HaveCount(3);

        var newTile = result.Settings.Tiles.FirstOrDefault(t => t.TileId == "newautoshow");
        newTile.Should().NotBeNull();
        newTile!.IsVisible.Should().BeTrue();
        newTile.DisplayOrder.Should().Be(2); // appended after max order 1

        capturedUpdate.Should().NotBeNull();
        capturedUpdate!.LastModified.Should().Be(FixedUtcNow);
        var backfilledTile = capturedUpdate.Tiles.FirstOrDefault(t => t.TileId == "newautoshow");
        backfilledTile.Should().NotBeNull();
        backfilledTile!.LastModified.Should().Be(FixedUtcNow);

        _repositoryMock.Verify(x => x.UpdateAsync(existingSettings), Times.Once);
        _repositoryMock.Verify(x => x.AddAsync(It.IsAny<UserDashboardSettings>()), Times.Never);
    }

    [Fact]
    public async Task Handle_AcquiresLockOncePerCall()
    {
        // Arrange
        var userId = "user123";
        var request = new GetUserSettingsRequest { UserId = userId };

        _tileRegistryMock.Setup(x => x.GetAvailableTiles()).Returns(new List<TileMetadata>());
        _repositoryMock
            .Setup(x => x.GetByUserIdAsync(userId))
            .ReturnsAsync((UserDashboardSettings?)null);
        _repositoryMock
            .Setup(x => x.AddAsync(It.IsAny<UserDashboardSettings>()))
            .ReturnsAsync((UserDashboardSettings s) => s);

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        _lockMock.Verify(x => x.AcquireAsync(userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static TileMetadata MakeTile(string tileId, bool defaultEnabled = true, bool autoShow = true) =>
        new(tileId, tileId, $"{tileId} description", TileSize.Medium, TileCategory.Finance,
            defaultEnabled, autoShow, typeof(object), Array.Empty<string>());

    private static UserDashboardSettings CreateExistingUserSettings(string userId)
    {
        return new UserDashboardSettings
        {
            UserId = userId,
            LastModified = FixedUtcNow.AddHours(-1),
            Tiles = new List<UserDashboardTile>
            {
                new()
                {
                    UserId = userId,
                    TileId = "auto1",
                    IsVisible = true,
                    DisplayOrder = 0,
                    LastModified = FixedUtcNow.AddHours(-2)
                },
                new()
                {
                    UserId = userId,
                    TileId = "auto2",
                    IsVisible = false,
                    DisplayOrder = 1,
                    LastModified = FixedUtcNow.AddHours(-2)
                }
            }
        };
    }
}
