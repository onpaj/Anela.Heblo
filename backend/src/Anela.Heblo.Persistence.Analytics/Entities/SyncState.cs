namespace Anela.Heblo.Persistence.Analytics.Entities;

public class SyncState
{
    public string EntityName { get; set; } = "";
    public DateTimeOffset? Watermark { get; set; }
    public DateTimeOffset? LastRunStartedAt { get; set; }
    public DateTimeOffset? LastRunFinishedAt { get; set; }
    public string? LastRunStatus { get; set; }
    public int? LastRunRowsFetched { get; set; }
    public int? LastRunRowsUpserted { get; set; }
    public string? LastErrorMessage { get; set; }
}
