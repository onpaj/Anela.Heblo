using Anela.Heblo.Application.Features.Dashboard.Contracts;
using Anela.Heblo.Domain.Features.Dashboard;
using Anela.Heblo.Xcc.Services.Dashboard;

namespace Anela.Heblo.Application.Features.Dashboard.Infrastructure;

/// <summary>
/// Pure default-settings rules for the dashboard: which tiles are auto-shown, how a new user's
/// settings are built and how missing AutoShow tiles are back-filled for existing users.
/// </summary>
internal static class UserDashboardSettingsDefaults
{
    public static IReadOnlyList<TileMetadata> AutoShowTiles(ITileRegistry tileRegistry)
        => tileRegistry.GetAvailableTiles()
            .Where(t => t.DefaultEnabled && t.AutoShow)
            .ToList();

    public static UserDashboardSettings BuildDefault(string userId, IReadOnlyList<TileMetadata> autoShowTiles, DateTime now)
    {
        var settings = new UserDashboardSettings
        {
            UserId = userId,
            LastModified = now,
            Tiles = autoShowTiles.Select((tile, index) => new UserDashboardTile
            {
                UserId = userId,
                TileId = tile.TileId,
                IsVisible = true,
                DisplayOrder = index,
                LastModified = now,
                DashboardSettings = null!
            }).ToList()
        };

        foreach (var tile in settings.Tiles)
            tile.DashboardSettings = settings;

        return settings;
    }

    /// <summary>Appends missing AutoShow tiles to <paramref name="settings"/>. Returns true when anything changed.</summary>
    public static bool BackfillMissing(UserDashboardSettings settings, IReadOnlyList<TileMetadata> autoShowTiles, DateTime now)
    {
        var missing = MissingTiles(settings, autoShowTiles);
        if (missing.Count == 0)
            return false;

        var maxOrder = MaxOrder(settings);
        for (var i = 0; i < missing.Count; i++)
        {
            settings.Tiles.Add(new UserDashboardTile
            {
                UserId = settings.UserId,
                TileId = missing[i].TileId,
                IsVisible = true,
                DisplayOrder = maxOrder + i + 1,
                LastModified = now,
                DashboardSettings = settings
            });
        }

        settings.LastModified = now;
        return true;
    }

    /// <summary>
    /// Returns existing tiles plus missing AutoShow tiles as DTOs without mutating
    /// <paramref name="settings"/> (which may be a tracked EF entity).
    /// </summary>
    public static IReadOnlyList<UserDashboardTileDto> ProjectMissing(UserDashboardSettings settings, IReadOnlyList<TileMetadata> autoShowTiles, DateTime now)
    {
        var result = settings.Tiles.Select(t => new UserDashboardTileDto
        {
            TileId = t.TileId,
            IsVisible = t.IsVisible,
            DisplayOrder = t.DisplayOrder
        }).ToList();

        var missing = MissingTiles(settings, autoShowTiles);
        var maxOrder = MaxOrder(settings);
        for (var i = 0; i < missing.Count; i++)
        {
            result.Add(new UserDashboardTileDto
            {
                TileId = missing[i].TileId,
                IsVisible = true,
                DisplayOrder = maxOrder + i + 1
            });
        }

        return result;
    }

    private static List<TileMetadata> MissingTiles(UserDashboardSettings settings, IReadOnlyList<TileMetadata> autoShowTiles)
    {
        var existing = settings.Tiles.Select(t => t.TileId).ToHashSet();
        return autoShowTiles.Where(t => !existing.Contains(t.TileId)).ToList();
    }

    private static int MaxOrder(UserDashboardSettings settings)
        => settings.Tiles.Count > 0 ? settings.Tiles.Max(t => t.DisplayOrder) : -1;
}
