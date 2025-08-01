namespace Anela.Heblo.Xcc.Audit;

public class DataLoadAuditEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string DataType { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public int RecordCount { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public Dictionary<string, object> Parameters { get; set; } = new();
    public TimeSpan Duration { get; set; }
}