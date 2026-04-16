namespace Anela.Heblo.Xcc.Services.Dashboard;

public class DashboardOptions
{
    public const string SectionName = "Dashboard";

    public int MaxConcurrentTileLoads { get; set; } = 4;

    /// <summary>
    /// Maximum seconds to wait for a single tile's data to load before returning an error tile.
    /// Prevents one slow tile from blocking the entire dashboard response.
    /// </summary>
    public int TileLoadTimeoutSeconds { get; set; } = 5;
}
