namespace Anela.Heblo.Xcc.Services;

public class BackgroundJobInfo
{
    public string Id { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTime? EnqueuedAt { get; set; }
    public DateTime? ScheduledAt { get; set; }
    public string? Arguments { get; set; }
    public string? Exception { get; set; }
}

public class QueuedJobsResult
{
    public List<BackgroundJobInfo> Jobs { get; set; } = new();
    public int TotalCount { get; set; }
}

public class GetQueuedJobsRequest
{
    public int Offset { get; set; } = 0;
    public int Count { get; set; } = 50;
    public string? Queue { get; set; } = "default";
    public string? State { get; set; }
}

public class GetScheduledJobsRequest
{
    public int Offset { get; set; } = 0;
    public int Count { get; set; } = 50;
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
}

public class GetJobRequest
{
    public string JobId { get; set; } = string.Empty;
    public bool IncludeHistory { get; set; } = false;
}

public class GetFailedJobsRequest
{
    public int Offset { get; set; } = 0;
    public int Count { get; set; } = 50;
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
}