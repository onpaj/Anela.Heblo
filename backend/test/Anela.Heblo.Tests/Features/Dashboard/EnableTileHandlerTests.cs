using Anela.Heblo.Application.Features.Dashboard.UseCases.EnableTile;
using Anela.Heblo.Xcc.Services.Dashboard;
using Anela.Heblo.Xcc.Domain;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Dashboard;

public class EnableTileHandlerTests
{
    private readonly Mock<IDashboardService> _dashboardServiceMock;
    private readonly EnableTileHandler _handler;

    public EnableTileHandlerTests()
    {
        _dashboardServiceMock = new Mock<IDashboardService>();
        _handler = new EnableTileHandler(_dashboardServiceMock.Object);
    }

    [Fact]
    public async Task Handle_WhenUserIdIsNull_ShouldUseAnonymous()
    {
        // Arrange
        var request = new EnableTileRequest { UserId = null, TileId = "tile1" };
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
        var request = new EnableTileRequest { UserId = "", TileId = "tile1" };
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
    public async Task Handle_WhenTileExists_ShouldEnableTile()
    {
        // Arrange
        var userId = "user123";
        var tileId = "tile1";
        var request = new EnableTileRequest { UserId = userId, TileId = tileId };
        var userSettings = CreateSampleUserSettings(userId);
        
        // Ensure tile exists but is disabled
        userSettings.Tiles.First(t => t.TileId == tileId).IsVisible = false;
        
        _dashboardServiceMock
            .Setup(x => x.GetUserSettingsAsync(userId))
            .ReturnsAsync(userSettings);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        
        var enabledTile = userSettings.Tiles.First(t => t.TileId == tileId);
        enabledTile.IsVisible.Should().BeTrue();
        
        _dashboardServiceMock.Verify(x => x.GetUserSettingsAsync(userId), Times.Once);
        _dashboardServiceMock.Verify(x => x.SaveUserSettingsAsync(userId, userSettings), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenTileDoesNotExist_ShouldAddNewTile()
    {
        // Arrange
        var userId = "user123";
        var tileId = "new-tile";
        var request = new EnableTileRequest { UserId = userId, TileId = tileId };
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
        
        userSettings.Tiles.Should().HaveCount(originalTileCount + 1);
        var newTile = userSettings.Tiles.First(t => t.TileId == tileId);
        newTile.IsVisible.Should().BeTrue();
        newTile.UserId.Should().Be(userId);
        newTile.DisplayOrder.Should().Be(originalTileCount); // Should be added at the end
        newTile.LastModified.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        
        _dashboardServiceMock.Verify(x => x.SaveUserSettingsAsync(userId, userSettings), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenTileAlreadyEnabled_ShouldNotChangeState()
    {
        // Arrange
        var userId = "user123";
        var tileId = "tile1";
        var request = new EnableTileRequest { UserId = userId, TileId = tileId };
        var userSettings = CreateSampleUserSettings(userId);
        
        // Ensure tile is already enabled
        userSettings.Tiles.First(t => t.TileId == tileId).IsVisible = true;
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
        tile.IsVisible.Should().BeTrue();
        tile.LastModified.Should().BeAfter(originalLastModified); // Should still update LastModified
        
        _dashboardServiceMock.Verify(x => x.SaveUserSettingsAsync(userId, userSettings), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenTileIdIsNull_ShouldReturnFailure()
    {
        // Arrange
        var request = new EnableTileRequest { UserId = "user123", TileId = null };

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
        var request = new EnableTileRequest { UserId = "user123", TileId = "" };

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
                    IsVisible = false,
                    DisplayOrder = 0,
                    LastModified = DateTime.UtcNow.AddHours(-1)
                },
                new()
                {
                    UserId = userId,
                    TileId = "tile2",
                    IsVisible = true,
                    DisplayOrder = 1,
                    LastModified = DateTime.UtcNow.AddHours(-1)
                }
            }
        };
    }
}