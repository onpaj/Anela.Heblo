namespace Anela.Heblo.Domain.Features.Attendance;

public class LogetoCreateTimeEntryRequest
{
    public required Guid Person { get; init; }
    public required Guid Activity { get; init; }
    public required DateOnly Date { get; init; }
    public string? From { get; init; }
    public string? To { get; init; }
    public required bool Billable { get; init; }
    public string? Description { get; init; }
    public string? ExternalKey { get; init; }
}
