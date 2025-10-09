namespace Anela.Heblo.API.Controllers;

public class RefreshTaskStatusDto
{
    public required string TaskId { get; init; }
    public required bool Enabled { get; init; }
    public string? Description { get; init; }
    public required TimeSpan RefreshInterval { get; init; }
    public RefreshTaskExecutionLogDto? LastExecution { get; init; }
}