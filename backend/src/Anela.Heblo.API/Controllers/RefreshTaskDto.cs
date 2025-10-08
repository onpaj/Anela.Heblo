namespace Anela.Heblo.API.Controllers;

public class RefreshTaskDto
{
    public required string TaskId { get; init; }
    public required TimeSpan InitialDelay { get; init; }
    public required TimeSpan RefreshInterval { get; init; }
    public required bool Enabled { get; init; }
}