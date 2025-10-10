using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Xcc.Services.Dashboard;

public interface IDashboardService
{
    Task<UserDashboardSettings> GetUserSettingsAsync(string userId);
    Task SaveUserSettingsAsync(string userId, UserDashboardSettings settings);
    Task<IEnumerable<TileData>> GetTileDataAsync(string userId);
}

public class TileData
{
    public string TileId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public TileSize Size { get; set; }
    public TileCategory Category { get; set; }
    public bool DefaultEnabled { get; set; }
    public bool AutoShow { get; set; }
    public Type ComponentType { get; set; } = typeof(object);
    public string[] RequiredPermissions { get; set; } = Array.Empty<string>();
    public object Data { get; set; } = new();
}