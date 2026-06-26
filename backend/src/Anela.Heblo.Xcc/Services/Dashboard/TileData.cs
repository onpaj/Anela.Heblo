namespace Anela.Heblo.Xcc.Services.Dashboard;

public class TileData
{
    public string TileId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public TileSize Size { get; set; }
    public TileCategory Category { get; set; }
    public bool DefaultEnabled { get; set; }
    public bool AutoShow { get; set; }
    public string[] RequiredPermissions { get; set; } = Array.Empty<string>();
    public bool IsUnauthorized { get; set; }
    public object Data { get; set; } = new();
}