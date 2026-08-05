namespace Anela.Heblo.Application.Features.Attendance;

public class BreakInsertionOptions
{
    public const string ConfigKey = "Logeto:BreakInsertion";

    /// <summary>First day of the daily walk (fixed start date; idempotent skipping keeps re-runs cheap).</summary>
    public DateOnly StartDate { get; set; } = new(2026, 8, 1);

    /// <summary>People whose Note equals this marker (trimmed, case-insensitive) are processed.</summary>
    public string NoteMarker { get; set; } = "integration";

    /// <summary>Name of the Break-type Logeto activity to insert. Account has "Přestávka" (generic,
    /// used here) and "Oběd" (lunch) — see spike results doc for why the generic one was chosen.</summary>
    public string BreakActivityName { get; set; } = "Přestávka";

    /// <summary>Preferred break start, Prague wall clock.</summary>
    public TimeOnly PreferredWindowStart { get; set; } = new(11, 0);

    public int BreakDurationMinutes { get; set; } = 30;

    /// <summary>Daily worked-hours threshold (inclusive) that requires a break.</summary>
    public int MinWorkHours { get; set; } = 6;

    /// <summary>Whether API From/To timestamps are UTC. False = local wall time — confirmed by the
    /// verification spike (docs/superpowers/specs/2026-08-05-logeto-spike-results.md): the API is a
    /// pure pass-through of Prague wall-clock time, verified against the live Logeto UI.</summary>
    public bool ApiTimesAreUtc { get; set; } = false;
}
