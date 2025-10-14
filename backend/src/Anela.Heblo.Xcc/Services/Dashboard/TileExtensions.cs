namespace Anela.Heblo.Xcc.Services.Dashboard;

public static class TileExtensions
{
    public static string GetTileId(this Type tileType) => tileType.Name.ToLowerInvariant().Replace("tile", "");
    public static string GetTileId<TTile>() => typeof(TTile).GetTileId();
    public static string GetTileId(this ITile tile) => tile.GetType().GetTileId();
}