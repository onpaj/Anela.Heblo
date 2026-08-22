using System.Globalization;

namespace Anela.Heblo.Domain.Features.Attendance;

/// <summary>
/// A Logeto person's Note field carries both the integration opt-in marker and that
/// person's net daily contracted hours (úvazek): "integration 6,4". Logeto's public API
/// exposes úvazek nowhere — the Úvazky pracovníků screen is web-app-only — so the Note is
/// the single place this lives. See
/// docs/superpowers/specs/2026-08-10-logeto-absence-hours-design.md.
/// </summary>
public class IntegrationNote
{
    /// <summary>Marker used by callers that have no configurable NoteMarker of their own.</summary>
    public const string DefaultMarker = "integration";

    private const double MinDailyHours = 0;
    private const double MaxDailyHours = 24;

    private static readonly IntegrationNote NotEnrolledNote = new() { IsEnrolled = false };

    public bool IsEnrolled { get; private init; }

    /// <summary>Net daily hours, or null when the note carries no usable number.</summary>
    public TimeSpan? DailyHours { get; private init; }

    public static IntegrationNote Parse(string? note) => Parse(note, DefaultMarker);

    public static IntegrationNote Parse(string? note, string marker)
    {
        var trimmed = note?.Trim();

        if (string.IsNullOrEmpty(trimmed)
            || !trimmed.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
        {
            return NotEnrolledNote;
        }

        var remainder = trimmed[marker.Length..];

        // "integrationX" is a different note, not an enrolled person with a typo.
        if (remainder.Length > 0 && !char.IsWhiteSpace(remainder[0]))
        {
            return NotEnrolledNote;
        }

        return new IntegrationNote
        {
            IsEnrolled = true,
            DailyHours = ParseDailyHours(remainder.Trim())
        };
    }

    private static TimeSpan? ParseDailyHours(string text)
    {
        if (text.Length == 0)
        {
            return null;
        }

        // Czech notes use a decimal comma; accept both separators regardless of server locale.
        var normalized = text.Replace(',', '.');

        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var hours)
            || hours <= MinDailyHours
            || hours > MaxDailyHours)
        {
            return null;
        }

        // Round to whole minutes: the API rejects non-zero seconds, and 6.4 is not exactly
        // representable in binary floating point.
        return TimeSpan.FromMinutes(Math.Round(hours * 60));
    }
}
