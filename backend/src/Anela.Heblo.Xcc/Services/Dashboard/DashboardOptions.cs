namespace Anela.Heblo.Xcc.Services.Dashboard;

public class DashboardOptions
{
    public const string SectionName = "Dashboard";

    public int MaxConcurrentTileLoads { get; set; } = 4;

    /// <summary>
    /// Maximum time in seconds allowed for a single tile to load its data.
    /// If a tile exceeds this limit, an error tile is returned instead of blocking the response.
    /// </summary>
    public int TileLoadTimeoutSeconds { get; set; } = 5;
}
