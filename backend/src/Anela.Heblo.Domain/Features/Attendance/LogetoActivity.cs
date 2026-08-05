namespace Anela.Heblo.Domain.Features.Attendance;

public class LogetoActivity
{
    public Guid Guid { get; init; }
    public string? Name { get; init; }
    public string Type { get; init; } = string.Empty;
    public bool Inactive { get; init; }
}
