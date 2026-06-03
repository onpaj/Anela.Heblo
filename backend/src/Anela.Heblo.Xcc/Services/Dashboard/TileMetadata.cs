namespace Anela.Heblo.Xcc.Services.Dashboard;

public sealed record TileMetadata(
    string TileId,
    string Title,
    string Description,
    TileSize Size,
    TileCategory Category,
    bool DefaultEnabled,
    bool AutoShow,
    string[] RequiredPermissions);
