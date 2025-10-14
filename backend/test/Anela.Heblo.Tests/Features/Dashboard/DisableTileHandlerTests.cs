using Anela.Heblo.Application.Features.Dashboard.UseCases.DisableTile;
using Anela.Heblo.Xcc.Services.Dashboard;
using Anela.Heblo.Xcc.Domain;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Dashboard;

public class DisableTileHandlerTests
{
    private readonly Mock<IDashboardService> _dashboardServiceMock;
    private readonly DisableTileHandler _handler;

    public DisableTileHandlerTests()
    {
        _dashboardServiceMock = new Mock<IDashboardService>();
        _handler = new DisableTileHandler(_dashboardServiceMock.Object, TimeProvider.System);
    }

    [Fact]
    public async Task Handle_WhenUserIdIsNull_ShouldUseAnonymous()
    {
        // Arrange
        var request = new DisableTileRequest { UserId = null, TileId = "tile1" };
        var userSettings = CreateSampleUserSettings("anonymous");

        _dashboardServiceMock
            .Setup(x => x.GetUserSettingsAsync("anonymous"))
            .ReturnsAsync(userSettings);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        _dashboardServiceMock.Verify(x => x.GetUserSettingsAsync("anonymous"), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenUserIdIsEmpty_ShouldUseAnonymous()
    {
        // Arrange
        var request = new DisableTileRequest { UserId = "", TileId = "tile1" };
        var userSettings = CreateSampleUserSettings("anonymous");

        _dashboardServiceMock
            .Setup(x => x.GetUserSettingsAsync("anonymous"))
            .ReturnsAsync(userSettings);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        _dashboardServiceMock.Verify(x => x.GetUserSettingsAsync("anonymous"), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenTileExists_ShouldDisableTile()
    {
        // Arrange
        var userId = "user123";
        var tileId = "tile1";
        var request = new DisableTileRequest { UserId = userId, TileId = tileId };
        var userSettings = CreateSampleUserSettings(userId);

        // Ensure tile exists and is enabled
        userSettings.Tiles.First(t => t.TileId == tileId).IsVisible = true;

        _dashboardServiceMock
            .Setup(x => x.GetUserSettingsAsync(userId))
            .ReturnsAsync(userSettings);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();

        var disabledTile = userSettings.Tiles.First(t => t.TileId == tileId);
        disabledTile.IsVisible.Should().BeFalse();

        _dashboardServiceMock.Verify(x => x.GetUserSettingsAsync(userId), Times.Once);
        _dashboardServiceMock.Verify(x => x.SaveUserSettingsAsync(userId, userSettings), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenTileDoesNotExist_ShouldNotChangeSettings()
    {
        // Arrange
        var userId = "user123";
        var tileId = "new-tile";
        var request = new DisableTileRequest { UserId = userId, TileId = tileId };
        var userSettings = CreateSampleUserSettings(userId);
        var originalTileCount = userSettings.Tiles.Count;

        _dashboardServiceMock
            .Setup(x => x.GetUserSettingsAsync(userId))
            .ReturnsAsync(userSettings);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();

        // Should not create a new tile - just return success without changing anything
        userSettings.Tiles.Should().HaveCount(originalTileCount);
        userSettings.Tiles.Should().NotContain(t => t.TileId == tileId);

        // Should not call SaveUserSettingsAsync since no changes were made
        _dashboardServiceMock.Verify(x => x.SaveUserSettingsAsync(userId, userSettings), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenTileAlreadyDisabled_ShouldNotChangeState()
    {
        // Arrange
        var userId = "user123";
        var tileId = "tile1";
        var request = new DisableTileRequest { UserId = userId, TileId = tileId };
        var userSettings = CreateSampleUserSettings(userId);

        // Ensure tile is already disabled
        userSettings.Tiles.First(t => t.TileId == tileId).IsVisible = false;
        var originalLastModified = userSettings.Tiles.First(t => t.TileId == tileId).LastModified;

        _dashboardServiceMock
            .Setup(x => x.GetUserSettingsAsync(userId))
            .ReturnsAsync(userSettings);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();

        var tile = userSettings.Tiles.First(t => t.TileId == tileId);
        tile.IsVisible.Should().BeFalse();
        tile.LastModified.Should().BeAfter(originalLastModified); // Should still update LastModified

        _dashboardServiceMock.Verify(x => x.SaveUserSettingsAsync(userId, userSettings), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenTileIdIsNull_ShouldReturnFailure()
    {
        // Arrange
        var request = new DisableTileRequest { UserId = "user123", TileId = null };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        // ErrorMessage property not available

        _dashboardServiceMock.Verify(x => x.GetUserSettingsAsync(It.IsAny<string>()), Times.Never);
        _dashboardServiceMock.Verify(x => x.SaveUserSettingsAsync(It.IsAny<string>(), It.IsAny<UserDashboardSettings>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenTileIdIsEmpty_ShouldReturnFailure()
    {
        // Arrange
        var request = new DisableTileRequest { UserId = "user123", TileId = "" };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        // ErrorMessage property not available

        _dashboardServiceMock.Verify(x => x.GetUserSettingsAsync(It.IsAny<string>()), Times.Never);
        _dashboardServiceMock.Verify(x => x.SaveUserSettingsAsync(It.IsAny<string>(), It.IsAny<UserDashboardSettings>()), Times.Never);
    }

    private static UserDashboardSettings CreateSampleUserSettings(string userId)
    {
        return new UserDashboardSettings
        {
            UserId = userId,
            LastModified = DateTime.UtcNow,
            Tiles = new List<UserDashboardTile>
            {
                new()
                {
                    UserId = userId,
                    TileId = "tile1",
                    IsVisible = true,
                    DisplayOrder = 0,
                    LastModified = DateTime.UtcNow.AddHours(-1)
                },
                new()
                {
                    UserId = userId,
                    TileId = "tile2",
                    IsVisible = false,
                    DisplayOrder = 1,
                    LastModified = DateTime.UtcNow.AddHours(-1)
                }
            }
        };
    }
}