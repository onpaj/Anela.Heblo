using System.Collections.Concurrent;
using Anela.Heblo.Xcc.Domain;
using Anela.Heblo.Xcc.Services.Concurrency;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Xcc.Services.Dashboard;

public class DashboardService : IDashboardService
{
    private readonly ITileRegistry _tileRegistry;
    private readonly IUserDashboardSettingsRepository _settingsRepository;
    private readonly int _maxConcurrentTileLoads;
    private readonly IKeyedAsyncLock _lockPool;
    private readonly TimeSpan _lockTtl;

    public DashboardService(
        ITileRegistry tileRegistry,
        IUserDashboardSettingsRepository settingsRepository,
        IOptions<DashboardOptions> dashboardOptions,
        IKeyedAsyncLock lockPool)
    {
        _tileRegistry = tileRegistry;
        _settingsRepository = settingsRepository;
        _maxConcurrentTileLoads = dashboardOptions.Value.MaxConcurrentTileLoads;
        _lockPool = lockPool;
        _lockTtl = TimeSpan.FromMinutes(dashboardOptions.Value.UserLockSlidingExpirationMinutes);
    }

    public async Task<UserDashboardSettings> GetUserSettingsAsync(string userId)
    {
        await using (await _lockPool.AcquireAsync($"dashboard:{userId}", _lockTtl))
        {
            var settings = await _settingsRepository.GetByUserIdAsync(userId);
            var availableTiles = _tileRegistry.GetAvailableTiles();
            var autoShowTiles = availableTiles
                .Where(t => t.DefaultEnabled && t.AutoShow)
                .ToList();

            if (settings == null)
            {
                settings = new UserDashboardSettings
                {
                    UserId = userId,
                    LastModified = DateTime.UtcNow
                };

                var tileSettings = autoShowTiles.Select((tile, index) => new UserDashboardTile
                {
                    UserId = userId,
                    TileId = tile.TileId,
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
                var existingTileIds = settings.Tiles.Select(t => t.TileId).ToHashSet();
                var newAutoShowTiles = autoShowTiles
                    .Where(t => !existingTileIds.Contains(t.TileId))
                    .ToList();

                if (newAutoShowTiles.Any())
                {
                    var maxOrder = settings.Tiles.Any() ? settings.Tiles.Max(t => t.DisplayOrder) : -1;
                    var newTileSettings = newAutoShowTiles.Select((tile, index) => new UserDashboardTile
                    {
                        UserId = userId,
                        TileId = tile.TileId,
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
    }

    public async Task SaveUserSettingsAsync(string userId, UserDashboardSettings settings)
    {
        await using (await _lockPool.AcquireAsync($"dashboard:{userId}", _lockTtl))
        {
            settings.UserId = userId;
            settings.LastModified = DateTime.UtcNow;
            await _settingsRepository.UpdateAsync(settings);
        }
    }

    public async Task<IEnumerable<TileData>> GetTileDataAsync(string userId, Dictionary<string, string>? tileParameters = null)
    {
        var settings = await GetUserSettingsAsync(userId);
        var visibleTiles = settings.Tiles
            .Where(t => t.IsVisible)
            .OrderBy(t => t.DisplayOrder)
            .ToList();

        var results = new ConcurrentBag<(int Index, TileData Data)>();

        await Parallel.ForEachAsync(
            visibleTiles.Select((tile, index) => (tile, index)),
            new ParallelOptions { MaxDegreeOfParallelism = _maxConcurrentTileLoads },
            async (item, ct) =>
            {
                var (tileSettings, index) = item;

                try
                {
                    var metadata = _tileRegistry.GetTileMetadata(tileSettings.TileId);
                    if (metadata == null)
                    {
                        results.Add((index, new TileData
                        {
                            TileId = tileSettings.TileId,
                            Title = "Error",
                            Description = $"Tile '{tileSettings.TileId}' not found",
                            Size = TileSize.Small,
                            Category = TileCategory.Error,
                            Data = new { Error = $"Tile '{tileSettings.TileId}' not found" }
                        }));
                        return;
                    }

                    var data = await _tileRegistry.GetTileDataAsync(tileSettings.TileId, tileParameters);

                    results.Add((index, new TileData
                    {
                        TileId = metadata.TileId,
                        Title = metadata.Title,
                        Description = metadata.Description,
                        Size = metadata.Size,
                        Category = metadata.Category,
                        DefaultEnabled = metadata.DefaultEnabled,
                        AutoShow = metadata.AutoShow,
                        ComponentType = metadata.ComponentType,
                        RequiredPermissions = metadata.RequiredPermissions,
                        Data = data
                    }));
                }
                catch (Exception ex)
                {
                    results.Add((index, new TileData
                    {
                        TileId = tileSettings.TileId,
                        Title = "Error",
                        Description = $"Failed to load tile: {ex.Message}",
                        Size = TileSize.Small,
                        Category = TileCategory.Error,
                        Data = new { Error = ex.Message }
                    }));
                }
            });

        return results.OrderBy(r => r.Index).Select(r => r.Data);
    }
}
