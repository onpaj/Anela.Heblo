using Anela.Heblo.Xcc.Services.Dashboard;
using Anela.Heblo.Xcc.Domain;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Dashboard;

public class DashboardServiceTests
{
    private readonly Mock<ITileRegistry> _tileRegistryMock;
    private readonly Mock<IUserDashboardSettingsRepository> _settingsRepositoryMock;
    private readonly DashboardService _service;

    public DashboardServiceTests()
    {
        _tileRegistryMock = new Mock<ITileRegistry>();
        _settingsRepositoryMock = new Mock<IUserDashboardSettingsRepository>();
        _service = new DashboardService(_tileRegistryMock.Object, _settingsRepositoryMock.Object);
    }

    [Fact]
    public async Task GetUserSettingsAsync_WhenUserNotExists_ShouldCreateDefaultSettings()
    {
        // Arrange
        var userId = "user123";
        var autoShowTiles = CreateMockAutoShowTiles();
        
        _settingsRepositoryMock
            .Setup(x => x.GetByUserIdAsync(userId))
            .ReturnsAsync((UserDashboardSettings)null);
        
        _tileRegistryMock
            .Setup(x => x.GetAvailableTiles())
            .Returns(autoShowTiles);

        // Act
        var result = await _service.GetUserSettingsAsync(userId);

        // Assert
        result.Should().NotBeNull();
        result.UserId.Should().Be(userId);
        result.Tiles.Should().HaveCount(2);
        result.Tiles.All(t => t.IsVisible).Should().BeTrue();
        result.Tiles.All(t => t.UserId == userId).Should().BeTrue();
        
        var tiles = result.Tiles.OrderBy(t => t.DisplayOrder).ToArray();
        tiles[0].TileId.Should().Be("auto1");
        tiles[0].DisplayOrder.Should().Be(0);
        tiles[1].TileId.Should().Be("auto2");
        tiles[1].DisplayOrder.Should().Be(1);
        
        _settingsRepositoryMock.Verify(x => x.AddAsync(It.IsAny<UserDashboardSettings>()), Times.Once);
    }

    [Fact]
    public async Task GetUserSettingsAsync_WhenUserExists_ShouldReturnExistingSettings()
    {
        // Arrange
        var userId = "user123";
        var existingSettings = CreateExistingUserSettings(userId);
        
        _settingsRepositoryMock
            .Setup(x => x.GetByUserIdAsync(userId))
            .ReturnsAsync(existingSettings);
        
        _tileRegistryMock
            .Setup(x => x.GetAvailableTiles())
            .Returns(new List<ITile>());

        // Act
        var result = await _service.GetUserSettingsAsync(userId);

        // Assert
        result.Should().Be(existingSettings);
        result.UserId.Should().Be(userId);
        result.Tiles.Should().HaveCount(2);
        
        _settingsRepositoryMock.Verify(x => x.AddAsync(It.IsAny<UserDashboardSettings>()), Times.Never);
        _settingsRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<UserDashboardSettings>()), Times.Never);
    }

    [Fact]
    public async Task GetUserSettingsAsync_WhenNewAutoShowTilesAvailable_ShouldAddThem()
    {
        // Arrange
        var userId = "user123";
        var existingSettings = CreateExistingUserSettings(userId);
        var allTiles = CreateMockAutoShowTiles();
        
        // Add a new auto-show tile that the user doesn't have yet
        var newTile = new NewAutoShowTile();
        allTiles.Add(newTile);
        
        _settingsRepositoryMock
            .Setup(x => x.GetByUserIdAsync(userId))
            .ReturnsAsync(existingSettings);
        
        _tileRegistryMock
            .Setup(x => x.GetAvailableTiles())
            .Returns(allTiles);

        // Act
        var result = await _service.GetUserSettingsAsync(userId);

        // Assert
        result.Should().Be(existingSettings);
        result.Tiles.Should().HaveCount(3); // Original 2 + 1 new
        
        var newAddedTile = result.Tiles.FirstOrDefault(t => t.TileId == "newautoshow");
        newAddedTile.Should().NotBeNull();
        newAddedTile.IsVisible.Should().BeTrue();
        newAddedTile.DisplayOrder.Should().Be(2); // Should be added after existing tiles
        
        _settingsRepositoryMock.Verify(x => x.UpdateAsync(existingSettings), Times.Once);
    }

    [Fact]
    public async Task GetUserSettingsAsync_WhenNoAutoShowTiles_ShouldNotAddAnyTiles()
    {
        // Arrange
        var userId = "user123";
        var tilesWithoutAutoShow = new List<ITile>();
        var nonAutoShowTile = new ManualTile();
        tilesWithoutAutoShow.Add(nonAutoShowTile);
        
        _settingsRepositoryMock
            .Setup(x => x.GetByUserIdAsync(userId))
            .ReturnsAsync((UserDashboardSettings)null);
        
        _tileRegistryMock
            .Setup(x => x.GetAvailableTiles())
            .Returns(tilesWithoutAutoShow);

        // Act
        var result = await _service.GetUserSettingsAsync(userId);

        // Assert
        result.Should().NotBeNull();
        result.UserId.Should().Be(userId);
        result.Tiles.Should().BeEmpty();
        
        _settingsRepositoryMock.Verify(x => x.AddAsync(It.IsAny<UserDashboardSettings>()), Times.Once);
    }

    [Fact]
    public async Task SaveUserSettingsAsync_ShouldUpdateUserIdAndTimestamp()
    {
        // Arrange
        var userId = "user123";
        var settings = new UserDashboardSettings
        {
            UserId = "old-user",
            LastModified = DateTime.UtcNow.AddDays(-1),
            Tiles = new List<UserDashboardTile>()
        };

        // Act
        await _service.SaveUserSettingsAsync(userId, settings);

        // Assert
        settings.UserId.Should().Be(userId);
        settings.LastModified.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        
        _settingsRepositoryMock.Verify(x => x.UpdateAsync(settings), Times.Once);
    }

    [Fact]
    public async Task GetTileDataAsync_WhenUserHasNoSettings_ShouldReturnEmptyList()
    {
        // Arrange
        var userId = "user123";
        _settingsRepositoryMock
            .Setup(x => x.GetByUserIdAsync(userId))
            .ReturnsAsync((UserDashboardSettings)null);
        
        _tileRegistryMock
            .Setup(x => x.GetAvailableTiles())
            .Returns(new List<ITile>());

        // Act
        var result = await _service.GetTileDataAsync(userId);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTileDataAsync_WhenUserHasVisibleTiles_ShouldReturnTileData()
    {
        // Arrange
        var userId = "user123";
        var userSettings = CreateUserSettingsWithVisibleTiles(userId);
        var mockTile = CreateMockTileWithData("tile1");
        
        _settingsRepositoryMock
            .Setup(x => x.GetByUserIdAsync(userId))
            .ReturnsAsync(userSettings);
        
        _tileRegistryMock
            .Setup(x => x.GetAvailableTiles())
            .Returns(new List<ITile>());
        
        _tileRegistryMock
            .Setup(x => x.GetTile("tile1"))
            .Returns(mockTile);
        
        _tileRegistryMock
            .Setup(x => x.GetTileDataAsync("tile1"))
            .ReturnsAsync(new { Status = "Active", Count = 42 });

        // Act
        var result = await _service.GetTileDataAsync(userId);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(1);
        
        var tileData = result.First();
        tileData.TileId.Should().Be("testwithdata");
        tileData.Title.Should().Be("Test Tile");
        tileData.Data.Should().NotBeNull();
    }

    [Fact]
    public async Task GetTileDataAsync_WhenTileNotFound_ShouldReturnErrorTile()
    {
        // Arrange
        var userId = "user123";
        var userSettings = CreateUserSettingsWithVisibleTiles(userId);
        
        _settingsRepositoryMock
            .Setup(x => x.GetByUserIdAsync(userId))
            .ReturnsAsync(userSettings);
        
        _tileRegistryMock
            .Setup(x => x.GetAvailableTiles())
            .Returns(new List<ITile>());
        
        _tileRegistryMock
            .Setup(x => x.GetTile("tile1"))
            .Returns((ITile)null);

        // Act
        var result = await _service.GetTileDataAsync(userId);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(1);
        
        var errorTile = result.First();
        errorTile.TileId.Should().Be("tile1");
        errorTile.Title.Should().Be("Error");
        errorTile.Category.Should().Be(TileCategory.Error);
        errorTile.Data.Should().NotBeNull();
    }

    [Fact]
    public async Task GetTileDataAsync_WhenTileDataLoadingThrows_ShouldReturnErrorTile()
    {
        // Arrange
        var userId = "user123";
        var userSettings = CreateUserSettingsWithVisibleTiles(userId);
        var mockTile = CreateMockTileWithData("tile1");
        
        _settingsRepositoryMock
            .Setup(x => x.GetByUserIdAsync(userId))
            .ReturnsAsync(userSettings);
        
        _tileRegistryMock
            .Setup(x => x.GetAvailableTiles())
            .Returns(new List<ITile>());
        
        _tileRegistryMock
            .Setup(x => x.GetTile("tile1"))
            .Returns(mockTile);
        
        _tileRegistryMock
            .Setup(x => x.GetTileDataAsync("tile1"))
            .ThrowsAsync(new InvalidOperationException("Data loading failed"));

        // Act
        var result = await _service.GetTileDataAsync(userId);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(1);
        
        var errorTile = result.First();
        errorTile.TileId.Should().Be("tile1");
        errorTile.Title.Should().Be("Error");
        errorTile.Category.Should().Be(TileCategory.Error);
        errorTile.Description.Should().Contain("Data loading failed");
    }

    private static List<ITile> CreateMockAutoShowTiles()
    {
        var tiles = new List<ITile>();
        
        var tile1 = new AutoTile1();
        tiles.Add(tile1);
        
        var tile2 = new AutoTile2();
        tiles.Add(tile2);
        
        return tiles;
    }

    private static UserDashboardSettings CreateExistingUserSettings(string userId)
    {
        return new UserDashboardSettings
        {
            UserId = userId,
            LastModified = DateTime.UtcNow.AddHours(-1),
            Tiles = new List<UserDashboardTile>
            {
                new()
                {
                    UserId = userId,
                    TileId = "auto1",
                    IsVisible = true,
                    DisplayOrder = 0,
                    LastModified = DateTime.UtcNow.AddHours(-2)
                },
                new()
                {
                    UserId = userId,
                    TileId = "auto2",
                    IsVisible = false,
                    DisplayOrder = 1,
                    LastModified = DateTime.UtcNow.AddHours(-2)
                }
            }
        };
    }

    private static UserDashboardSettings CreateUserSettingsWithVisibleTiles(string userId)
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
                }
            }
        };
    }

    private static TestTileWithData CreateMockTileWithData(string tileId)
    {
        return new TestTileWithData(tileId);
    }
}

// Test tile classes for DashboardServiceTests
public class NewAutoShowTile : ITile
{
    public string Title { get; init; } = "Auto Show Tile";
    public string Description { get; init; } = "Auto show tile description";
    public TileSize Size { get; init; } = TileSize.Medium;
    public TileCategory Category { get; init; } = TileCategory.Finance;
    public bool DefaultEnabled { get; init; } = true;
    public bool AutoShow { get; init; } = true;
    public Type ComponentType { get; init; } = typeof(object);
    public string[] RequiredPermissions { get; init; } = Array.Empty<string>();
    
    public Task<object> LoadDataAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult((object)"Auto show data");
    }
}

public class ManualTile : ITile
{
    public string Title { get; init; } = "Manual Tile";
    public string Description { get; init; } = "Manual tile description";
    public TileSize Size { get; init; } = TileSize.Medium;
    public TileCategory Category { get; init; } = TileCategory.Finance;
    public bool DefaultEnabled { get; init; } = true;
    public bool AutoShow { get; init; } = false;
    public Type ComponentType { get; init; } = typeof(object);
    public string[] RequiredPermissions { get; init; } = Array.Empty<string>();
    
    public Task<object> LoadDataAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult((object)"Manual data");
    }
}

public class AutoTile1 : ITile
{
    public string Title { get; init; } = "Auto Tile 1";
    public string Description { get; init; } = "Auto tile 1 description";
    public TileSize Size { get; init; } = TileSize.Medium;
    public TileCategory Category { get; init; } = TileCategory.Finance;
    public bool DefaultEnabled { get; init; } = true;
    public bool AutoShow { get; init; } = true;
    public Type ComponentType { get; init; } = typeof(object);
    public string[] RequiredPermissions { get; init; } = Array.Empty<string>();
    
    public Task<object> LoadDataAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult((object)"Auto tile 1 data");
    }
}

public class AutoTile2 : ITile
{
    public string Title { get; init; } = "Auto Tile 2";
    public string Description { get; init; } = "Auto tile 2 description";
    public TileSize Size { get; init; } = TileSize.Medium;
    public TileCategory Category { get; init; } = TileCategory.Finance;
    public bool DefaultEnabled { get; init; } = true;
    public bool AutoShow { get; init; } = true;
    public Type ComponentType { get; init; } = typeof(object);
    public string[] RequiredPermissions { get; init; } = Array.Empty<string>();
    
    public Task<object> LoadDataAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult((object)"Auto tile 2 data");
    }
}

public class TestTileWithData : ITile
{
    public string TileId { get; }
    public string Title { get; init; } = "Test Tile";
    public string Description { get; init; } = "Test Description";
    public TileSize Size { get; init; } = TileSize.Medium;
    public TileCategory Category { get; init; } = TileCategory.Finance;
    public bool DefaultEnabled { get; init; } = true;
    public bool AutoShow { get; init; } = false;
    public Type ComponentType { get; init; } = typeof(object);
    public string[] RequiredPermissions { get; init; } = Array.Empty<string>();
    
    public TestTileWithData(string tileId)
    {
        TileId = tileId;
    }
    
    public Task<object> LoadDataAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult((object)$"Test data for {TileId}");
    }
}