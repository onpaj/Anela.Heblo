using Anela.Heblo.Application.Features.Dashboard.Contracts;
using Anela.Heblo.Application.Features.Dashboard.UseCases.SaveUserSettings;
using Anela.Heblo.Xcc.Services.Dashboard;
using Anela.Heblo.Xcc.Domain;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Dashboard;

public class SaveUserSettingsHandlerTests
{
    private readonly Mock<IDashboardService> _dashboardServiceMock;
    private readonly SaveUserSettingsHandler _handler;

    public SaveUserSettingsHandlerTests()
    {
        _dashboardServiceMock = new Mock<IDashboardService>();
        _handler = new SaveUserSettingsHandler(_dashboardServiceMock.Object, TimeProvider.System);
    }

    [Fact]
    public async Task Handle_WhenUserIdIsNull_ShouldUseAnonymous()
    {
        // Arrange
        var request = new SaveUserSettingsRequest
        {
            UserId = null,
            Tiles = new[]
            {
                new UserDashboardTileDto { TileId = "tile1", IsVisible = true, DisplayOrder = 0 }
            }
        };
        
        var existingSettings = CreateSampleUserSettings("anonymous");
        _dashboardServiceMock
            .Setup(x => x.GetUserSettingsAsync("anonymous"))
            .ReturnsAsync(existingSettings);

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        _dashboardServiceMock.Verify(x => x.SaveUserSettingsAsync(
            "anonymous",
            It.Is<UserDashboardSettings>(s => 
                s.UserId == "anonymous" && 
                s.Tiles.Count == 1 &&
                s.Tiles.First().TileId == "tile1")), 
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenUserIdIsEmpty_ShouldUseAnonymous()
    {
        // Arrange
        var request = new SaveUserSettingsRequest
        {
            UserId = "",
            Tiles = new[]
            {
                new UserDashboardTileDto { TileId = "tile1", IsVisible = true, DisplayOrder = 0 }
            }
        };
        
        var existingSettings = CreateSampleUserSettings("anonymous");
        _dashboardServiceMock
            .Setup(x => x.GetUserSettingsAsync("anonymous"))
            .ReturnsAsync(existingSettings);

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        _dashboardServiceMock.Verify(x => x.SaveUserSettingsAsync(
            "anonymous",
            It.Is<UserDashboardSettings>(s => s.UserId == "anonymous")), 
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenValidUserId_ShouldSaveSettings()
    {
        // Arrange
        var userId = "user123";
        var request = new SaveUserSettingsRequest
        {
            UserId = userId,
            Tiles = new[]
            {
                new UserDashboardTileDto { TileId = "tile1", IsVisible = true, DisplayOrder = 0 },
                new UserDashboardTileDto { TileId = "tile2", IsVisible = false, DisplayOrder = 1 }
            }
        };
        
        var existingSettings = CreateSampleUserSettings(userId);
        _dashboardServiceMock
            .Setup(x => x.GetUserSettingsAsync(userId))
            .ReturnsAsync(existingSettings);

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        _dashboardServiceMock.Verify(x => x.SaveUserSettingsAsync(
            userId,
            It.Is<UserDashboardSettings>(s => 
                s.UserId == userId &&
                s.Tiles.Count == 2 &&
                s.Tiles.Any(t => t.TileId == "tile1" && t.IsVisible && t.DisplayOrder == 0) &&
                s.Tiles.Any(t => t.TileId == "tile2" && !t.IsVisible && t.DisplayOrder == 1))), 
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenNoTiles_ShouldSaveEmptySettings()
    {
        // Arrange
        var userId = "user123";
        var request = new SaveUserSettingsRequest
        {
            UserId = userId,
            Tiles = Array.Empty<UserDashboardTileDto>()
        };
        
        var existingSettings = CreateSampleUserSettings(userId);
        _dashboardServiceMock
            .Setup(x => x.GetUserSettingsAsync(userId))
            .ReturnsAsync(existingSettings);

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        _dashboardServiceMock.Verify(x => x.SaveUserSettingsAsync(
            userId,
            It.Is<UserDashboardSettings>(s => 
                s.UserId == userId &&
                s.Tiles.Count == 0)), 
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenNullTiles_ShouldSaveEmptySettings()
    {
        // Arrange
        var userId = "user123";
        var request = new SaveUserSettingsRequest
        {
            UserId = userId,
            Tiles = null
        };
        
        var existingSettings = CreateSampleUserSettings(userId);
        _dashboardServiceMock
            .Setup(x => x.GetUserSettingsAsync(userId))
            .ReturnsAsync(existingSettings);

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        _dashboardServiceMock.Verify(x => x.SaveUserSettingsAsync(
            userId,
            It.Is<UserDashboardSettings>(s => 
                s.UserId == userId &&
                s.Tiles.Count == 0)), 
            Times.Once);
    }

    [Fact]
    public async Task Handle_ShouldMapTilePropertiesCorrectly()
    {
        // Arrange
        var userId = "user123";
        var request = new SaveUserSettingsRequest
        {
            UserId = userId,
            Tiles = new[]
            {
                new UserDashboardTileDto 
                { 
                    TileId = "analytics-tile", 
                    IsVisible = true, 
                    DisplayOrder = 5 
                }
            }
        };
        
        var existingSettings = CreateSampleUserSettings(userId);
        _dashboardServiceMock
            .Setup(x => x.GetUserSettingsAsync(userId))
            .ReturnsAsync(existingSettings);

        UserDashboardSettings capturedSettings = null;
        _dashboardServiceMock
            .Setup(x => x.SaveUserSettingsAsync(It.IsAny<string>(), It.IsAny<UserDashboardSettings>()))
            .Callback<string, UserDashboardSettings>((_, settings) => capturedSettings = settings);

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        capturedSettings.Should().NotBeNull();
        capturedSettings.UserId.Should().Be(userId);
        capturedSettings.Tiles.Should().HaveCount(1);
        
        var tile = capturedSettings.Tiles.First();
        tile.UserId.Should().Be(userId);
        tile.TileId.Should().Be("analytics-tile");
        tile.IsVisible.Should().BeTrue();
        tile.DisplayOrder.Should().Be(5);
        tile.LastModified.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Handle_ShouldReturnSuccessResponse()
    {
        // Arrange
        var request = new SaveUserSettingsRequest
        {
            UserId = "user123",
            Tiles = new[]
            {
                new UserDashboardTileDto { TileId = "tile1", IsVisible = true, DisplayOrder = 0 }
            }
        };
        
        var existingSettings = CreateSampleUserSettings("user123");
        _dashboardServiceMock
            .Setup(x => x.GetUserSettingsAsync("user123"))
            .ReturnsAsync(existingSettings);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
    }

    private static UserDashboardSettings CreateSampleUserSettings(string userId)
    {
        return new UserDashboardSettings
        {
            UserId = userId,
            LastModified = DateTime.UtcNow,
            Tiles = new List<UserDashboardTile>()
        };
    }
}