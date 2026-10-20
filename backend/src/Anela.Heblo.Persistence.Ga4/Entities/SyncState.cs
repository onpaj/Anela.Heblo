namespace Anela.Heblo.Persistence.Ga4.Entities;

/// <summary>
/// Watermark and last-run bookkeeping, one row per ingested table. Mirrors
/// <c>Anela.Heblo.Persistence.Analytics.Entities.SyncState</c> so the two schemas read alike.
/// </summary>
public class SyncState
{
    public string EntityName { get; set; } = "";

    /// <summary>
    /// The most recent date that has been fully ingested. The next run resumes at
    /// <c>Watermark - TrailingReprocessDays</c>, never at <c>Watermark + 1</c>, because GA4
    /// keeps reprocessing recent days.
    /// </summary>
    public DateOnly? WatermarkDate { get; set; }

    public DateTimeOffset? LastRunStartedAt { get; set; }
    public DateTimeOffset? LastRunFinishedAt { get; set; }
    public string? LastRunStatus { get; set; }
    public int? LastRunRowsFetched { get; set; }
    public int? LastRunRowsUpserted { get; set; }

    /// <summary>The per-day top-N cap in force for this table, or null when the table is not capped.</summary>
    public int? TopNPerDay { get; set; }

    public string? LastErrorMessage { get; set; }
}
