namespace Anela.Heblo.Xcc.Services.Dashboard;

public interface ITileRegistry
{
    void RegisterTile<TTile>() where TTile : class, ITile;
    IEnumerable<ITile> GetAvailableTiles();
    ITile? GetTile(string tileId);
    Task<object?> GetTileDataAsync(string tileId, Dictionary<string, string>? parameters = null);
    IEnumerable<string> GetRegisteredTileIds();
}