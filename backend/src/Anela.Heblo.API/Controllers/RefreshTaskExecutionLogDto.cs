namespace Anela.Heblo.API.Controllers;

public class RefreshTaskExecutionLogDto
{
    public required string TaskId { get; init; }
    public required DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public required string Status { get; init; }
    public string? ErrorMessage { get; init; }
    public TimeSpan? Duration { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}