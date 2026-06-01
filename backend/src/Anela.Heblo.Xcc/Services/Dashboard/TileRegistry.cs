using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Xcc.Services.Dashboard;

public class TileRegistry : ITileRegistry
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<string, Type> _registeredTiles = new();

    public TileRegistry(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public void RegisterTile<TTile>() where TTile : class, ITile
    {
        var tileType = typeof(TTile);
        var tileId = tileType.GetTileId();

        _registeredTiles[tileId] = tileType;
    }

    public IEnumerable<TileMetadata> GetAvailableTiles()
    {
        var result = new List<TileMetadata>(_registeredTiles.Count);
        using var scope = _serviceProvider.CreateScope();
        foreach (var tileType in _registeredTiles.Values)
        {
            var tile = (ITile)scope.ServiceProvider.GetRequiredService(tileType);
            result.Add(ToMetadata(tile));
        }
        return result;
    }

    public TileMetadata? GetTileMetadata(string tileId)
    {
        if (!_registeredTiles.TryGetValue(tileId, out var tileType))
        {
            return null;
        }

        using var scope = _serviceProvider.CreateScope();
        var tile = (ITile)scope.ServiceProvider.GetRequiredService(tileType);
        return ToMetadata(tile);
    }

    public async Task<object?> GetTileDataAsync(string tileId, Dictionary<string, string>? parameters = null)
    {
        if (!_registeredTiles.TryGetValue(tileId, out var tileType))
        {
            return null;
        }

        using var scope = _serviceProvider.CreateScope();
        var tile = (ITile)scope.ServiceProvider.GetRequiredService(tileType);
        return await tile.LoadDataAsync(parameters);
    }

    private static TileMetadata ToMetadata(ITile tile) => new(
        tile.GetTileId(),
        tile.Title,
        tile.Description,
        tile.Size,
        tile.Category,
        tile.DefaultEnabled,
        tile.AutoShow,
        tile.ComponentType,
        tile.RequiredPermissions);

    public IEnumerable<string> GetRegisteredTileIds()
    {
        return _registeredTiles.Keys;
    }
}