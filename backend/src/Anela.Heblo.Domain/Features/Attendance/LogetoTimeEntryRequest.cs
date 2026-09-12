namespace Anela.Heblo.Domain.Features.Attendance;

/// <summary>
/// Logeto uses one TimeTrackingRequest body for both POST (create) and PUT (full replacement),
/// so this type serves both. A PUT replaces the record wholesale, which is why every preserved
/// field has to be resent — see docs/superpowers/specs/2026-08-10-logeto-absence-hours-design.md.
/// </summary>
public class LogetoTimeEntryRequest
{
    public required Guid Person { get; init; }
    public required Guid Activity { get; init; }
    public required DateOnly Date { get; init; }
    public string? From { get; init; }
    public string? To { get; init; }

    /// <summary>Duration for records entered without a clock window. The API requires a seconds
    /// component and requires it to be zero, so the format is "HH:mm:00".</summary>
    public string? Hours { get; init; }

    public required bool Billable { get; init; }
    public string? Description { get; init; }
    public string? ExternalKey { get; init; }
    public Guid? Contract { get; init; }
    public Guid? Subcontract { get; init; }
}
