namespace Anela.Heblo.Domain.Features.Attendance;

public class LogetoTimeEntry
{
    public Guid Guid { get; init; }
    public Guid Person { get; init; }
    public DateOnly Date { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }

    /// <summary>Duration for records entered without a clock window, e.g. "08:04:00".</summary>
    public string? Hours { get; init; }

    public Guid Activity { get; init; }
    public string? Description { get; init; }
    public string? ExternalKey { get; init; }

    // Billable/Contract/Subcontract are read back only so an update can resend them unchanged:
    // the Logeto PUT is a full replacement, not a patch.
    public bool Billable { get; init; }
    public Guid? Contract { get; init; }
    public Guid? Subcontract { get; init; }
}
