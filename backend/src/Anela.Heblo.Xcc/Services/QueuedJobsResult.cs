namespace Anela.Heblo.Xcc.Services;

public class QueuedJobsResult
{
    public List<BackgroundJobInfo> Jobs { get; set; } = new();
    public int TotalCount { get; set; }
}