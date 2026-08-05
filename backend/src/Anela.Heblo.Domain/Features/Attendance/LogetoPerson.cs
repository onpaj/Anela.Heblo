namespace Anela.Heblo.Domain.Features.Attendance;

public class LogetoPerson
{
    public Guid Guid { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Note { get; init; }
    public bool Inactive { get; init; }
}
