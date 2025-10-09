namespace Anela.Heblo.API.Controllers.Dashboard;

public class DashboardTileDto
{
    public string TileId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty; // "Small", "Medium", "Large"
    public string Category { get; set; } = string.Empty;
    public bool DefaultEnabled { get; set; }
    public bool AutoShow { get; set; }
    public string[] RequiredPermissions { get; set; } = Array.Empty<string>();
    public object? Data { get; set; }
}