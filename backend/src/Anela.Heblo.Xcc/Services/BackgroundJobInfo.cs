namespace Anela.Heblo.Xcc.Services;

public class BackgroundJobInfo
{
    public string Id { get; set; } = string.Empty;
    public string JobName { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTime? CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public string Queue { get; set; } = string.Empty;
}