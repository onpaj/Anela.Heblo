namespace Anela.Heblo.Application.Features.Attendance;

public class AbsenceHoursOptions
{
    public const string ConfigKey = "Logeto:AbsenceHours";

    /// <summary>First day of the daily walk (fixed start date; idempotent skipping keeps re-runs cheap).</summary>
    public DateOnly StartDate { get; set; } = new(2026, 8, 1);

    /// <summary>Days of history scanned before today. The walk covers
    /// [max(StartDate, today - LookbackDays), today - 1] — past-only, because a same-day absence
    /// may still be edited by the worker. A record that ages past this window without being filled
    /// is never revisited, so widening it is the way to backfill history.</summary>
    public int LookbackDays { get; set; } = 7;

    /// <summary>People whose Note starts with this marker (trimmed, case-insensitive) are processed.
    /// The rest of the note carries their net daily hours — see <see cref="Domain.Features.Attendance.IntegrationNote"/>.</summary>
    public string NoteMarker { get; set; } = "integration";
}
