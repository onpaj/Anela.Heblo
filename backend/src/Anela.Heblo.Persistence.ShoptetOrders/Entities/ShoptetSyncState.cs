namespace Anela.Heblo.Persistence.ShoptetOrders.Entities;

/// <summary>
/// Watermark row per synced entity, mirroring the flexi_raw sync_state contract.
/// </summary>
public class ShoptetSyncState
{
    public string EntityName { get; set; } = "";
    public DateTimeOffset? Watermark { get; set; }
    public DateTimeOffset? LastRunStartedAt { get; set; }
    public DateTimeOffset? LastRunFinishedAt { get; set; }
    public string? LastRunStatus { get; set; }
    public int? LastRunRowsFetched { get; set; }
    public int? LastRunRowsUpserted { get; set; }
    public string? LastErrorMessage { get; set; }

    /// <summary>
    /// Backfill cursor: the first day NOT yet backfilled. The backfill walks creation-time
    /// windows forward from ShoptetOrdersSyncOptions.BackfillFrom and persists its progress
    /// here after each window, so an interrupted run resumes where it stopped.
    /// </summary>
    public DateOnly? BackfillCursor { get; set; }
    public bool BackfillCompleted { get; set; }
}
