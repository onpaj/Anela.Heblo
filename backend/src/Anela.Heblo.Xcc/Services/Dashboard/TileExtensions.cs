using System.Reflection;

namespace Anela.Heblo.Xcc.Services.Dashboard;

public static class TileExtensions
{
    public static string GetTileId(this Type tileType)
    {
        var attr = tileType.GetCustomAttribute<TileIdAttribute>(inherit: false);
        if (attr is null)
            throw new InvalidOperationException(
                $"Tile type '{tileType.FullName}' is missing [TileId(\"...\")]. " +
                $"Every ITile must declare an explicit, stable identifier.");
        return attr.Value;
    }

    public static string GetTileId<TTile>() => typeof(TTile).GetTileId();
    public static string GetTileId(this ITile tile) => tile.GetType().GetTileId();
}