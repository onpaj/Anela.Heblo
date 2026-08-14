using Anela.Heblo.Domain.Features.Attendance;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Attendance.Services;

public class BreakInsertionService
{
    private readonly ILogetoClient _client;
    private readonly IOptions<BreakInsertionOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<BreakInsertionService> _logger;

    public BreakInsertionService(
        ILogetoClient client,
        IOptions<BreakInsertionOptions> options,
        TimeProvider timeProvider,
        ILogger<BreakInsertionService> logger)
    {
        _client = client;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<BreakInsertionSummary> RunAsync(CancellationToken cancellationToken)
    {
        var options = _options.Value;
        var summary = new BreakInsertionSummary();

        var pragueNow = TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), LogetoTimeConverter.PragueTimeZone);
        var today = DateOnly.FromDateTime(pragueNow.Date);
        var lookbackDays = Math.Max(options.LookbackDays, 0);
        var windowStart = today.AddDays(-lookbackDays);
        var from = windowStart < options.StartDate ? options.StartDate : windowStart;

        if (from > today)
        {
            _logger.LogWarning(
                "Break insertion window is empty: computed from {From} (StartDate {StartDate}) is after today {Today}. Nothing to do.",
                from, options.StartDate, today);
            return summary;
        }

        var activities = await _client.GetActivitiesAsync(cancellationToken);
        var breakActivity = activities.FirstOrDefault(a =>
                a.Type == LogetoActivityTypes.Break
                && string.Equals(a.Name?.Trim(), options.BreakActivityName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"Break activity '{options.BreakActivityName}' not found in Logeto or is not of type Break.");

        var typeByActivity = activities.ToDictionary(a => a.Guid, a => a.Type);

        var people = (await _client.GetPeopleAsync(cancellationToken))
            .Where(p => !p.Inactive && HasNoteMarker(p.Note, options.NoteMarker))
            .ToList();

        if (people.Count == 0)
        {
            _logger.LogWarning("No active Logeto workers found with note marker '{NoteMarker}'", options.NoteMarker);
            return summary;
        }

        var entries = await _client.GetTimeTrackingAsync(from, today, cancellationToken);

        foreach (var person in people)
        {
            var days = entries
                .Where(e => e.Person == person.Guid && e.Date >= from && e.Date <= today)
                .GroupBy(e => e.Date)
                .OrderBy(g => g.Key);

            foreach (var day in days)
            {
                try
                {
                    await ProcessDayAsync(
                        person, day.Key, day.ToList(), typeByActivity, breakActivity, options, summary,
                        today, cancellationToken);
                }
                catch (Exception ex)
                {
                    summary.Failed++;
                    _logger.LogError(ex,
                        "Failed to insert break for person {PersonGuid} on {Date}", person.Guid, day.Key);
                }
            }
        }

        _logger.LogInformation(
            "Break insertion finished: {Scanned} days scanned, {Inserted} breaks inserted, " +
            "{ExistingBreak} had a break, {InProgress} in progress, {BelowThreshold} below threshold, " +
            "{HoursOnly} hours-only, {NoSlot} no slot, {Failed} failed",
            summary.DaysScanned, summary.BreaksInserted, summary.SkippedExistingBreak,
            summary.SkippedInProgress, summary.SkippedBelowThreshold, summary.SkippedHoursOnly,
            summary.SkippedNoSlot, summary.Failed);

        return summary;
    }

    /// <summary>The Logeto Note is human-edited free text that also carries the person's úvazek
    /// ("integration 6.4"), so only its first whitespace-delimited word is the opt-in marker.</summary>
    private static bool HasNoteMarker(string? note, string marker)
    {
        var firstWord = note?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.Equals(firstWord, marker, StringComparison.OrdinalIgnoreCase);
    }

    private async Task ProcessDayAsync(
        LogetoPerson person,
        DateOnly date,
        IReadOnlyList<LogetoTimeEntry> dayEntries,
        IReadOnlyDictionary<Guid, string> typeByActivity,
        LogetoActivity breakActivity,
        BreakInsertionOptions options,
        BreakInsertionSummary summary,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        summary.DaysScanned++;

        if (dayEntries.Any(e => e.From.HasValue && !e.To.HasValue))
        {
            summary.SkippedInProgress++;

            if (date < today)
            {
                _logger.LogWarning(
                    "Skipping {Date} for person {PersonGuid}: an open record (no end time) is present, " +
                    "so the day was skipped. If it is still open on a later run, it needs a human to check in Logeto.",
                    date, person.Guid);
            }
            else
            {
                _logger.LogDebug(
                    "Skipping {Date} for person {PersonGuid}: an open record (no end time) is present — " +
                    "the worker is still at work.",
                    date, person.Guid);
            }

            return;
        }

        if (dayEntries.Any(e => typeByActivity.GetValueOrDefault(e.Activity) == LogetoActivityTypes.Break))
        {
            summary.SkippedExistingBreak++;
            return;
        }

        var workEntries = dayEntries
            .Where(e => typeByActivity.GetValueOrDefault(e.Activity) == LogetoActivityTypes.Work)
            .ToList();

        var windowed = workEntries
            .Where(e => e.From.HasValue && e.To.HasValue && e.To > e.From)
            .ToList();

        foreach (var invalid in workEntries.Where(e => e.From.HasValue && e.To.HasValue && e.To <= e.From))
        {
            _logger.LogWarning(
                "Ignoring work entry {EntryGuid} for person {PersonGuid} on {Date}: To ({To}) is not after From ({From})",
                invalid.Guid, person.Guid, date, invalid.To, invalid.From);
        }

        var windowedTotal = windowed.Aggregate(TimeSpan.Zero, (sum, e) => sum + (e.To!.Value - e.From!.Value));
        var hoursOnlyTotal = workEntries
            .Where(e => !e.From.HasValue || !e.To.HasValue)
            .Aggregate(TimeSpan.Zero, (sum, e) =>
                TimeSpan.TryParse(e.Hours, out var h) ? sum + h : sum);

        var threshold = TimeSpan.FromHours(options.MinWorkHours);

        if (windowedTotal + hoursOnlyTotal < threshold)
        {
            summary.SkippedBelowThreshold++;
            return;
        }

        if (windowedTotal < threshold)
        {
            summary.SkippedHoursOnly++;
            _logger.LogWarning(
                "Day {Date} for person {PersonGuid} reaches the threshold only with duration-only records; " +
                "cannot place a break automatically — fix manually in Logeto.",
                date, person.Guid);
            return;
        }

        var segments = BreakSlotCalculator.BuildSegments(windowed.Select(e => new TimeSlot(
            LogetoTimeConverter.ToPragueLocal(e.From!.Value, options.ApiTimesAreUtc),
            LogetoTimeConverter.ToPragueLocal(e.To!.Value, options.ApiTimesAreUtc))));

        var breakDuration = TimeSpan.FromMinutes(options.BreakDurationMinutes);
        var preferredStart = date.ToDateTime(options.PreferredWindowStart);
        var preferred = new TimeSlot(preferredStart, preferredStart + breakDuration);

        var slot = BreakSlotCalculator.ComputeBreakSlot(segments, preferred, breakDuration);
        if (slot is null)
        {
            summary.SkippedNoSlot++;
            _logger.LogWarning(
                "No suitable break slot found for person {PersonGuid} on {Date} (segments too short)",
                person.Guid, date);
            return;
        }

        var request = new LogetoCreateTimeEntryRequest
        {
            Person = person.Guid,
            Activity = breakActivity.Guid,
            Date = date,
            From = LogetoTimeConverter.ToApiTime(slot.Start, options.ApiTimesAreUtc),
            To = LogetoTimeConverter.ToApiTime(slot.End, options.ApiTimesAreUtc),
            Billable = false,
            Description = "Automatická přestávka",
            ExternalKey = $"autobreak-{person.Guid}-{date:yyyy-MM-dd}"
        };

        await _client.CreateTimeEntryAsync(request, merge: true, cancellationToken);
        summary.BreaksInserted++;

        _logger.LogInformation(
            "Inserted {Minutes}-minute break {From}–{To} for person {PersonGuid} on {Date}",
            options.BreakDurationMinutes, request.From, request.To, person.Guid, date);
    }
}

public class BreakInsertionSummary
{
    public int DaysScanned { get; set; }
    public int BreaksInserted { get; set; }
    public int SkippedExistingBreak { get; set; }
    public int SkippedInProgress { get; set; }
    public int SkippedBelowThreshold { get; set; }
    public int SkippedHoursOnly { get; set; }
    public int SkippedNoSlot { get; set; }
    public int Failed { get; set; }
}
