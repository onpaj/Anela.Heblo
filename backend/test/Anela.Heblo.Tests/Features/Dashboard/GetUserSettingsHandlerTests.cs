using Anela.Heblo.Application.Features.Dashboard.UseCases.GetUserSettings;
using Anela.Heblo.Xcc.Services.Dashboard;
using Anela.Heblo.Xcc.Domain;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Dashboard;

public class GetUserSettingsHandlerTests
{
    private readonly Mock<IDashboardService> _dashboardServiceMock;
    private readonly GetUserSettingsHandler _handler;

    public GetUserSettingsHandlerTests()
    {
        _dashboardServiceMock = new Mock<IDashboardService>();
        _handler = new GetUserSettingsHandler(_dashboardServiceMock.Object);
    }

    [Fact]
    public async Task Handle_WhenUserIdIsNull_ShouldUseAnonymous()
    {
        // Arrange
        var request = new GetUserSettingsRequest { UserId = null };
        var userSettings = CreateSampleUserSettings("anonymous");
        
        _dashboardServiceMock
            .Setup(x => x.GetUserSettingsAsync("anonymous"))
            .ReturnsAsync(userSettings);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Settings.Should().NotBeNull();
        _dashboardServiceMock.Verify(x => x.GetUserSettingsAsync("anonymous"), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenUserIdIsEmpty_ShouldUseAnonymous()
    {
        // Arrange
        var request = new GetUserSettingsRequest { UserId = "" };
        var userSettings = CreateSampleUserSettings("anonymous");
        
        _dashboardServiceMock
            .Setup(x => x.GetUserSettingsAsync("anonymous"))
            .ReturnsAsync(userSettings);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Settings.Should().NotBeNull();
        _dashboardServiceMock.Verify(x => x.GetUserSettingsAsync("anonymous"), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenValidUserId_ShouldReturnUserSettings()
    {
        // Arrange
        var userId = "user123";
        var request = new GetUserSettingsRequest { UserId = userId };
        var userSettings = CreateSampleUserSettings(userId);
        
        _dashboardServiceMock
            .Setup(x => x.GetUserSettingsAsync(userId))
            .ReturnsAsync(userSettings);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Settings.Should().NotBeNull();
        result.Settings.Tiles.Should().HaveCount(2);
        
        var tiles = result.Settings.Tiles.ToArray();
        tiles[0].TileId.Should().Be("tile1");
        tiles[0].IsVisible.Should().BeTrue();
        tiles[0].DisplayOrder.Should().Be(0);
        
        tiles[1].TileId.Should().Be("tile2");
        tiles[1].IsVisible.Should().BeFalse();
        tiles[1].DisplayOrder.Should().Be(1);
    }

    [Fact]
    public async Task Handle_WhenUserHasNoTiles_ShouldReturnEmptyTilesList()
    {
        // Arrange
        var userId = "user123";
        var request = new GetUserSettingsRequest { UserId = userId };
        var userSettings = new UserDashboardSettings
        {
            UserId = userId,
            LastModified = DateTime.UtcNow,
            Tiles = new List<UserDashboardTile>()
        };
        
        _dashboardServiceMock
            .Setup(x => x.GetUserSettingsAsync(userId))
            .ReturnsAsync(userSettings);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Settings.Should().NotBeNull();
        result.Settings.Tiles.Should().NotBeNull();
        result.Settings.Tiles.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ShouldCallDashboardServiceOnce()
    {
        // Arrange
        var userId = "user123";
        var request = new GetUserSettingsRequest { UserId = userId };
        var userSettings = CreateSampleUserSettings(userId);
        
        _dashboardServiceMock
            .Setup(x => x.GetUserSettingsAsync(userId))
            .ReturnsAsync(userSettings);

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        _dashboardServiceMock.Verify(x => x.GetUserSettingsAsync(userId), Times.Once);
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
                    LastModified = DateTime.UtcNow
                },
                new()
                {
                    UserId = userId,
                    TileId = "tile2",
                    IsVisible = false,
                    DisplayOrder = 1,
                    LastModified = DateTime.UtcNow
                }
            }
        };
    }
}