namespace Anela.Heblo.Application.Common.Cache;

public class RefreshTaskConfiguration
{
    public required string TaskId { get; init; }
    public required TimeSpan InitialDelay { get; init; }
    public required TimeSpan RefreshInterval { get; init; }
    public required bool Enabled { get; init; }
}