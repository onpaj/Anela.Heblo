using Anela.Heblo.Domain.Features.Attendance;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Attendance.Services;

/// <summary>
/// Absence records in Logeto (Dovolená, Nemoc, …) are routinely entered with no From/To and no
/// Hours, so a vacation day reads as an empty day worth zero hours. This service walks a rolling
/// window of past days and writes each opted-in person's net daily contracted hours into those
/// records. See docs/superpowers/specs/2026-08-10-logeto-absence-hours-design.md.
/// </summary>
public class AbsenceHoursService
{
    private readonly ILogetoClient _client;
    private readonly IOptions<AbsenceHoursOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AbsenceHoursService> _logger;

    public AbsenceHoursService(
        ILogetoClient client,
        IOptions<AbsenceHoursOptions> options,
        TimeProvider timeProvider,
        ILogger<AbsenceHoursService> logger)
    {
        _client = client;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<AbsenceHoursSummary> RunAsync(CancellationToken cancellationToken)
    {
        var options = _options.Value;
        var summary = new AbsenceHoursSummary();

        var pragueNow = TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), LogetoTimeConverter.PragueTimeZone);
        var today = DateOnly.FromDateTime(pragueNow.Date);
        var lookbackDays = Math.Max(options.LookbackDays, 0);
        var windowStart = today.AddDays(-lookbackDays);
        var from = windowStart < options.StartDate ? options.StartDate : windowStart;

        // Past-only: today is never entered, because the worker may still be editing it.
        var to = today.AddDays(-1);

        if (from > to)
        {
            _logger.LogWarning(
                "Absence hours window is empty: computed from {From} (StartDate {StartDate}) is after {To}. Nothing to do.",
                from, options.StartDate, to);
            return summary;
        }

        var absenceActivities = (await _client.GetActivitiesAsync(cancellationToken))
            .Where(a => a.Type == LogetoActivityTypes.Absence)
            .Select(a => a.Guid)
            .ToHashSet();

        var people = (await _client.GetPeopleAsync(cancellationToken))
            .Where(p => !p.Inactive)
            .Select(p => new { Person = p, Note = IntegrationNote.Parse(p.Note, options.NoteMarker) })
            .Where(x => x.Note.IsEnrolled)
            .ToList();

        if (people.Count == 0)
        {
            _logger.LogWarning("No active Logeto workers found with note marker '{NoteMarker}'", options.NoteMarker);
            return summary;
        }

        var entries = await _client.GetTimeTrackingAsync(from, to, cancellationToken);

        foreach (var enrolled in people)
        {
            var days = entries
                .Where(e => e.Person == enrolled.Person.Guid && e.Date >= from && e.Date <= to)
                .GroupBy(e => e.Date)
                .OrderBy(g => g.Key);

            foreach (var day in days)
            {
                try
                {
                    await ProcessDayAsync(
                        enrolled.Person, enrolled.Note.DailyHours, day.Key, day.ToList(),
                        absenceActivities, summary, cancellationToken);
                }
                catch (Exception ex)
                {
                    summary.Failed++;
                    _logger.LogError(ex,
                        "Failed to fill absence hours for person {PersonGuid} on {Date}",
                        enrolled.Person.Guid, day.Key);
                }
            }
        }

        _logger.LogInformation(
            "Absence hours finished: {Scanned} records scanned, {Filled} filled, " +
            "{NoHours} skipped for missing úvazek, {Ambiguous} ambiguous, {MixedDay} mixed days, {Failed} failed",
            summary.RecordsScanned, summary.HoursFilled, summary.SkippedNoHours,
            summary.SkippedAmbiguous, summary.SkippedMixedDay, summary.Failed);

        return summary;
    }

    private async Task ProcessDayAsync(
        LogetoPerson person,
        TimeSpan? dailyHours,
        DateOnly date,
        IReadOnlyList<LogetoTimeEntry> dayEntries,
        IReadOnlySet<Guid> absenceActivities,
        AbsenceHoursSummary summary,
        CancellationToken cancellationToken)
    {
        var empty = dayEntries.Where(e => IsEmptyAbsence(e, absenceActivities)).ToList();

        // The overwhelmingly common case — a normal working day. Not a skip, so it gets no
        // counter and no log line.
        if (empty.Count == 0)
        {
            return;
        }

        summary.RecordsScanned += empty.Count;

        // A half-day absence alongside half a day of work must not receive a full day's hours.
        if (empty.Count != dayEntries.Count)
        {
            summary.SkippedMixedDay++;
            _logger.LogWarning(
                "Skipping {Date} for {Person}: the day mixes an empty absence with other records, " +
                "so the absence may be partial — fill it manually in Logeto.",
                date, Describe(person));
            return;
        }

        if (empty.Count > 1)
        {
            summary.SkippedAmbiguous++;
            _logger.LogWarning(
                "Skipping {Date} for {Person}: {Count} empty absence records share the day and the " +
                "daily hours cannot be split between them without guessing.",
                date, Describe(person), empty.Count);
            return;
        }

        if (dailyHours is null)
        {
            summary.SkippedNoHours++;
            _logger.LogWarning(
                "Skipping {Date} for {Person}: their Logeto Note carries no daily hours. " +
                "Set it to e.g. 'integration 6,4' in Pracovníci → Note.",
                date, Describe(person));
            return;
        }

        var record = empty[0];

        // Full replacement: every preserved field is resent unchanged and only Hours is set.
        // No ExternalKey of our own is stamped — a keyed record breaks a later merge=true split
        // (spike Finding 2), and idempotency needs no marker because a record with Hours set no
        // longer matches the empty filter.
        var request = new LogetoTimeEntryRequest
        {
            Person = record.Person,
            Activity = record.Activity,
            Date = record.Date,
            Hours = FormatHours(dailyHours.Value),
            Billable = record.Billable,
            Description = record.Description,
            ExternalKey = record.ExternalKey,
            Contract = record.Contract,
            Subcontract = record.Subcontract
        };

        await _client.UpdateTimeEntryAsync(record.Guid, request, cancellationToken);
        summary.HoursFilled++;

        _logger.LogInformation(
            "Filled {Hours} into absence record {EntryGuid} for {Person} on {Date}",
            request.Hours, record.Guid, Describe(person), date);
    }

    private static bool IsEmptyAbsence(LogetoTimeEntry entry, IReadOnlySet<Guid> absenceActivities) =>
        absenceActivities.Contains(entry.Activity)
        && !entry.From.HasValue
        && !entry.To.HasValue
        && string.IsNullOrWhiteSpace(entry.Hours);

    /// <summary>The API requires a seconds component and requires it to be zero.</summary>
    private static string FormatHours(TimeSpan hours) =>
        $"{(int)hours.TotalHours:00}:{hours.Minutes:00}:00";

    private static string Describe(LogetoPerson person) =>
        $"{person.FirstName} {person.LastName} ({person.Guid})".TrimStart();
}

public class AbsenceHoursSummary
{
    public int RecordsScanned { get; set; }
    public int HoursFilled { get; set; }
    public int SkippedNoHours { get; set; }
    public int SkippedAmbiguous { get; set; }
    public int SkippedMixedDay { get; set; }
    public int Failed { get; set; }
}
