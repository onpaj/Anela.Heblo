namespace Anela.Heblo.Application.Features.Attendance.Services;

/// <summary>
/// Single conversion point between Logeto API timestamps and Prague wall-clock time.
/// The API's From/To representation (UTC vs local-with-Z) was determined by the
/// verification spike — see docs/superpowers/specs/2026-08-05-logeto-spike-results.md.
/// </summary>
public static class LogetoTimeConverter
{
    public static readonly TimeZoneInfo PragueTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague");

    public static DateTime ToPragueLocal(DateTimeOffset apiTime, bool apiTimesAreUtc) =>
        apiTimesAreUtc
            ? TimeZoneInfo.ConvertTime(apiTime, PragueTimeZone).DateTime
            : apiTime.DateTime;

    /// <summary>Formats a Prague wall-clock time for the API. Seconds are always :00 (API requirement).</summary>
    public static string ToApiTime(DateTime pragueLocal, bool apiTimesAreUtc)
    {
        if (!apiTimesAreUtc)
        {
            return pragueLocal.ToString("yyyy-MM-ddTHH:mm:00");
        }

        var utc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(pragueLocal, DateTimeKind.Unspecified), PragueTimeZone);
        return utc.ToString("yyyy-MM-ddTHH:mm:00Z");
    }
}
