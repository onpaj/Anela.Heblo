using Anela.Heblo.Xcc.Domain;
using System.Collections.Concurrent;

namespace Anela.Heblo.Xcc.Services.Dashboard;

public class DashboardService : IDashboardService
{
    private readonly ITileRegistry _tileRegistry;
    private readonly IUserDashboardSettingsRepository _settingsRepository;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _userLocks = new();

    public DashboardService(
        ITileRegistry tileRegistry,
        IUserDashboardSettingsRepository settingsRepository)
    {
        _tileRegistry = tileRegistry;
        _settingsRepository = settingsRepository;
    }

    private static SemaphoreSlim GetUserLock(string userId)
    {
        return _userLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
    }

    public async Task<UserDashboardSettings> GetUserSettingsAsync(string userId)
    {
        var userLock = GetUserLock(userId);
        await userLock.WaitAsync();
        
        try
        {
            var settings = await _settingsRepository.GetByUserIdAsync(userId);
            var availableTiles = _tileRegistry.GetAvailableTiles();
            var autoShowTiles = availableTiles
                .Where(t => t.DefaultEnabled && t.AutoShow)
                .ToList();

            if (settings == null)
            {
                // Create default settings for new user
                settings = new UserDashboardSettings
                {
                    UserId = userId,
                    LastModified = DateTime.UtcNow
                };

                // Create individual tile settings for AutoShow tiles
                var tileSettings = autoShowTiles.Select((tile, index) => new UserDashboardTile
                {
                    UserId = userId,
                    TileId = tile.GetTileId(),
                    IsVisible = true,
                    DisplayOrder = index,
                    LastModified = DateTime.UtcNow,
                    DashboardSettings = settings
                }).ToList();

                settings.Tiles = tileSettings;
                await _settingsRepository.AddAsync(settings);
            }
            else
            {
                // For existing users, add any new AutoShow tiles that aren't in their settings yet
                var existingTileIds = settings.Tiles.Select(t => t.TileId).ToHashSet();
                var newAutoShowTiles = autoShowTiles
                    .Where(t => !existingTileIds.Contains(t.GetTileId()))
                    .ToList();

                if (newAutoShowTiles.Any())
                {
                    var maxOrder = settings.Tiles.Any() ? settings.Tiles.Max(t => t.DisplayOrder) : -1;
                    var newTileSettings = newAutoShowTiles.Select((tile, index) => new UserDashboardTile
                    {
                        UserId = userId,
                        TileId = tile.GetTileId(),
                        IsVisible = true,
                        DisplayOrder = maxOrder + index + 1,
                        LastModified = DateTime.UtcNow,
                        DashboardSettings = settings
                    }).ToList();

                    foreach (var newTile in newTileSettings)
                    {
                        settings.Tiles.Add(newTile);
                    }

                    settings.LastModified = DateTime.UtcNow;
                    await _settingsRepository.UpdateAsync(settings);
                }
            }

            return settings;
        }
        finally
        {
            userLock.Release();
        }
    }

    public async Task SaveUserSettingsAsync(string userId, UserDashboardSettings settings)
    {
        var userLock = GetUserLock(userId);
        await userLock.WaitAsync();
        
        try
        {
            settings.UserId = userId;
            settings.LastModified = DateTime.UtcNow;
            await _settingsRepository.UpdateAsync(settings);
        }
        finally
        {
            userLock.Release();
        }
    }

    public async Task<IEnumerable<TileData>> GetTileDataAsync(string userId, Dictionary<string, string>? tileParameters = null)
    {
        var settings = await GetUserSettingsAsync(userId);
        var visibleTiles = settings.Tiles
            .Where(t => t.IsVisible)
            .OrderBy(t => t.DisplayOrder)
            .ToList();

        var result = new List<TileData>();

        foreach (var tileSettings in visibleTiles)
        {
            try
            {
                var tile = _tileRegistry.GetTile(tileSettings.TileId);
                if (tile == null)
                {
                    result.Add(new TileData
                    {
                        TileId = tileSettings.TileId,
                        Title = "Error",
                        Description = $"Tile '{tileSettings.TileId}' not found",
                        Size = TileSize.Small,
                        Category = TileCategory.Error,
                        Data = new { Error = $"Tile '{tileSettings.TileId}' not found" }
                    });
                    continue;
                }

                // Load data using registry method that manages scope properly
                var data = await _tileRegistry.GetTileDataAsync(tileSettings.TileId, tileParameters);

                result.Add(new TileData
                {
                    TileId = tile.GetTileId(),
                    Title = tile.Title,
                    Description = tile.Description,
                    Size = tile.Size,
                    Category = tile.Category,
                    DefaultEnabled = tile.DefaultEnabled,
                    AutoShow = tile.AutoShow,
                    ComponentType = tile.ComponentType,
                    RequiredPermissions = tile.RequiredPermissions,
                    Data = data
                });
            }
            catch (Exception ex)
            {
                // Log error but continue with other tiles
                result.Add(new TileData
                {
                    TileId = tileSettings.TileId,
                    Title = "Error",
                    Description = $"Failed to load tile: {ex.Message}",
                    Size = TileSize.Small,
                    Category = TileCategory.Error,
                    Data = new { Error = ex.Message }
                });
            }
        }

        return result;
    }
}