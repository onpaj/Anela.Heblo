namespace Anela.Heblo.Xcc.Services.Dashboard;

public class DashboardOptions
{
    public const string SectionName = "Dashboard";

    public int MaxConcurrentTileLoads { get; set; } = 4;

    /// <summary>
    /// Seconds to wait for a single tile to load before returning an error tile instead of blocking.
    /// </summary>
    public int TileLoadTimeoutSeconds { get; set; } = 5;

    /// <summary>
    /// Log a warning when a tile load exceeds this many seconds.
    /// </summary>
    public double SlowTileWarningThresholdSeconds { get; set; } = 2.0;
}
