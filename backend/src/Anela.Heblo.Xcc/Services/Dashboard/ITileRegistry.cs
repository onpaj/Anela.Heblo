namespace Anela.Heblo.Xcc.Services.Dashboard;

public interface ITileRegistry
{
    void RegisterTile<TTile>() where TTile : class, ITile;
    IEnumerable<TileMetadata> GetAvailableTiles();
    TileMetadata? GetTileMetadata(string tileId);
    Task<object?> GetTileDataAsync(string tileId, Dictionary<string, string>? parameters = null);
    IEnumerable<string> GetRegisteredTileIds();
}