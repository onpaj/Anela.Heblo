namespace Anela.Heblo.Application.Common.Cache;

public record RefreshTaskExecutionLog
{
    public required string TaskId { get; init; }
    public required DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public required RefreshTaskExecutionStatus Status { get; init; }
    public string? ErrorMessage { get; init; }
    public TimeSpan? Duration => CompletedAt.HasValue ? CompletedAt.Value - StartedAt : null;
    public Dictionary<string, object>? Metadata { get; init; }
}

public enum RefreshTaskExecutionStatus
{
    Running,
    Completed,
    Failed,
    Cancelled
}