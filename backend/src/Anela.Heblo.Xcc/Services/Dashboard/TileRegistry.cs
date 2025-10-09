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
        
        if (_registeredTiles.ContainsKey(tileId))
        {
            throw new InvalidOperationException($"Tile with ID '{tileId}' is already registered.");
        }
        
        _registeredTiles[tileId] = tileType;
    }

    public IEnumerable<ITile> GetAvailableTiles()
    {
        var tiles = new List<ITile>();
        
        using (var scope = _serviceProvider.CreateScope())
        {
            foreach (var tileType in _registeredTiles.Values)
            {
                var tile = (ITile)scope.ServiceProvider.GetRequiredService(tileType);
                tiles.Add(tile);
            }
        }
        
        return tiles;
    }

    public ITile? GetTile(string tileId)
    {
        if (!_registeredTiles.TryGetValue(tileId, out var tileType))
        {
            return null;
        }
        
        using (var scope = _serviceProvider.CreateScope())
        {
            return (ITile)scope.ServiceProvider.GetRequiredService(tileType);
        }
    }
    
    public async Task<object?> GetTileDataAsync(string tileId)
    {
        if (!_registeredTiles.TryGetValue(tileId, out var tileType))
        {
            return null;
        }
        
        using (var scope = _serviceProvider.CreateScope())
        {
            var tile = (ITile)scope.ServiceProvider.GetRequiredService(tileType);
            return await tile.LoadDataAsync();
        }
    }

    public IEnumerable<string> GetRegisteredTileIds()
    {
        return _registeredTiles.Keys;
    }
}