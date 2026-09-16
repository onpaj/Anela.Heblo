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

    /// <summary>Runs the nightly walk over the configured rolling window.</summary>
    public Task<BreakInsertionSummary> RunAsync(CancellationToken cancellationToken) =>
        RunAsync(lookbackDaysOverride: null, cancellationToken);

    /// <summary>
    /// Runs the walk, optionally over a wider window than the nightly one. Used for a deliberate
    /// sweep over history — the work is idempotent, so re-covering days already handled is free.
    /// </summary>
    public async Task<BreakInsertionSummary> RunAsync(
        int? lookbackDaysOverride, CancellationToken cancellationToken)
    {
        var options = _options.Value;
        var summary = new BreakInsertionSummary();

        var pragueNow = TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), LogetoTimeConverter.PragueTimeZone);
        var today = DateOnly.FromDateTime(pragueNow.Date);
        var lookbackDays = Math.Max(lookbackDaysOverride ?? options.LookbackDays, 0);
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
            .Where(p => !p.Inactive
                && IntegrationNote.Parse(p.Note, options.NoteMarker).IsEnrolled)
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
                        "Failed to process day {Date} for person {PersonGuid}", day.Key, person.Guid);
                }
            }
        }

        _logger.LogInformation(
            "Break insertion finished: {Scanned} days scanned, {Inserted} breaks inserted, " +
            "{Healed} stale days healed, {Touched} records touched ({TouchFailed} days failed to " +
            "touch), {ExistingBreak} already fine, {InProgress} in progress, {BelowThreshold} below " +
            "threshold, {HoursOnly} hours-only, {NoSlot} no slot, {Failed} failed",
            summary.DaysScanned, summary.BreaksInserted, summary.DaysHealed, summary.RecordsTouched,
            summary.TouchFailed, summary.SkippedExistingBreak, summary.SkippedInProgress,
            summary.SkippedBelowThreshold, summary.SkippedHoursOnly, summary.SkippedNoSlot,
            summary.Failed);

        return summary;
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

        var existingBreaks = dayEntries
            .Where(e => typeByActivity.GetValueOrDefault(e.Activity) == LogetoActivityTypes.Break)
            .ToList();

        if (existingBreaks.Count > 0)
        {
            // A break is already there. Its split may still be invisible to phones if a previous run
            // was interrupted before touching, or if the day predates this job's touch behaviour.
            //
            // Only breaks this job created are healed. A break a worker entered themselves was never
            // split by our merge=true call, so the Revision bug does not apply to it — and the
            // account-wide counter makes their own work record look "stale" purely because it was
            // written first. Without this guard the job would PUT records it has no business writing.
            var ownBreaks = existingBreaks
                .Where(e => e.ExternalKey == AutoBreakExternalKey(person.Guid, date))
                .ToList();

            try
            {
                var healed = await TouchSplitRecordsAsync(
                    person, date, dayEntries, ownBreaks, typeByActivity,
                    onlyStaleRevisions: true, options, summary, cancellationToken);

                // Day-level buckets stay mutually exclusive so they reconcile against DaysScanned;
                // RecordsTouched counts records and is deliberately a different unit.
                if (healed > 0)
                {
                    summary.DaysHealed++;
                }
                else
                {
                    summary.SkippedExistingBreak++;
                }
            }
            catch (Exception ex)
            {
                summary.TouchFailed++;
                _logger.LogError(ex,
                    "Failed to refresh the Revision of the work records around the break on {Date} " +
                    "for person {PersonGuid}. The day stays stale and is retried on the next run.",
                    date, person.Guid);
            }

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

        var request = new LogetoTimeEntryRequest
        {
            Person = person.Guid,
            Activity = breakActivity.Guid,
            Date = date,
            From = LogetoTimeConverter.ToApiTime(slot.Start, options.ApiTimesAreUtc),
            To = LogetoTimeConverter.ToApiTime(slot.End, options.ApiTimesAreUtc),
            Billable = false,
            Description = "Automatická přestávka",
            ExternalKey = AutoBreakExternalKey(person.Guid, date)
        };

        // merge=true lets Logeto split the work record around the break in one atomic operation.
        await _client.CreateTimeEntryAsync(request, merge: true, cancellationToken);
        summary.BreaksInserted++;

        _logger.LogInformation(
            "Inserted {Minutes}-minute break {From}–{To} for person {PersonGuid} on {Date}",
            options.BreakDurationMinutes, request.From, request.To, person.Guid, date);

        // ...but it does not bump the Revision of the record it rewrote in place, so phones never
        // refetch it. Re-read the day and touch the split's output to force a fresh Revision.
        //
        // The break itself is already written, so a failure from here on must not be reported as a
        // failed insert: the day is merely left stale, and the healing path picks it up next run.
        try
        {
            var afterSplit = (await _client.GetTimeTrackingAsync(date, date, cancellationToken))
                .Where(e => e.Person == person.Guid && e.Date == date)
                .ToList();

            var breaksAfterSplit = afterSplit
                .Where(e => typeByActivity.GetValueOrDefault(e.Activity) == LogetoActivityTypes.Break)
                .ToList();

            // A record created moments ago briefly reports Revision -1 before its real revision is
            // assigned, so the freshly split records are touched unconditionally rather than
            // compared against the break's revision, which is not yet meaningful.
            var touched = await TouchSplitRecordsAsync(
                person, date, afterSplit, breaksAfterSplit, typeByActivity,
                onlyStaleRevisions: false, options, summary, cancellationToken);

            if (touched == 0)
            {
                _logger.LogWarning(
                    "Break was inserted for person {PersonGuid} on {Date} but no work record adjacent to it " +
                    "was found to touch — phones may keep showing the pre-split day until the next run.",
                    person.Guid, date);
            }
        }
        catch (Exception ex)
        {
            summary.TouchFailed++;
            _logger.LogError(ex,
                "Break was inserted for person {PersonGuid} on {Date}, but refreshing the Revision of " +
                "the split's work records failed. The day stays stale until a later run heals it.",
                person.Guid, date);
        }
    }

    /// <summary>
    /// Bumps the <c>Revision</c> of the work records sitting either side of a break by writing them
    /// back unchanged. Logeto rewrites the surviving record in place without bumping it, so clients
    /// that sync incrementally never refetch it and keep showing the day as it was before the split.
    /// A no-op PUT changes nothing but the Revision — verified against the live account.
    /// </summary>
    private async Task<int> TouchSplitRecordsAsync(
        LogetoPerson person,
        DateOnly date,
        IReadOnlyList<LogetoTimeEntry> dayEntries,
        IReadOnlyList<LogetoTimeEntry> breaks,
        IReadOnlyDictionary<Guid, string> typeByActivity,
        bool onlyStaleRevisions,
        BreakInsertionOptions options,
        BreakInsertionSummary summary,
        CancellationToken cancellationToken)
    {
        var workEntries = dayEntries
            .Where(e => typeByActivity.GetValueOrDefault(e.Activity) == LogetoActivityTypes.Work
                && e.From.HasValue && e.To.HasValue)
            .ToList();

        // A record between two back-to-back breaks is adjacent to both, so collect the distinct set
        // first — one PUT is all it takes, and a live write is never worth issuing twice.
        var toTouch = new Dictionary<Guid, LogetoTimeEntry>();

        foreach (var brk in breaks.Where(b => b.From.HasValue && b.To.HasValue))
        {
            var adjacent = workEntries.Where(w => w.To == brk.From || w.From == brk.To);

            foreach (var work in adjacent)
            {
                if (onlyStaleRevisions && work.Revision >= brk.Revision)
                {
                    continue;
                }

                toTouch[work.Guid] = work;
            }
        }

        var touched = 0;

        foreach (var work in toTouch.Values)
        {
            await _client.UpdateTimeEntryAsync(
                work.Guid, BuildTouchRequest(work, options), cancellationToken);

            // Counted at the write itself: if a later write in this loop throws, the ones that
            // already landed must still show up in the summary.
            touched++;
            summary.RecordsTouched++;

            _logger.LogInformation(
                "Touched work record {EntryGuid} ({From}–{To}) for person {PersonGuid} on {Date} " +
                "to refresh its Revision",
                work.Guid, work.From, work.To, person.Guid, date);
        }

        return touched;
    }

    /// <summary>
    /// The key this job stamps on every break it creates. It is what tells our own breaks apart from
    /// the ones workers enter themselves, which must never be touched.
    /// </summary>
    private static string AutoBreakExternalKey(Guid personGuid, DateOnly date) =>
        $"autobreak-{personGuid}-{date:yyyy-MM-dd}";

    /// <summary>
    /// Resends a record exactly as it stands. A Logeto write is a full replacement, so every writable
    /// field is included; the response-only fields (Location, EndLocation, the rates) are not part of
    /// the request contract and are left untouched by the write.
    /// </summary>
    private static LogetoTimeEntryRequest BuildTouchRequest(
        LogetoTimeEntry source, BreakInsertionOptions options) => new()
        {
            Person = source.Person,
            Activity = source.Activity,
            Date = source.Date,
            From = LogetoTimeConverter.ToApiTime(
                LogetoTimeConverter.ToPragueLocal(source.From!.Value, options.ApiTimesAreUtc),
                options.ApiTimesAreUtc),
            To = LogetoTimeConverter.ToApiTime(
                LogetoTimeConverter.ToPragueLocal(source.To!.Value, options.ApiTimesAreUtc),
                options.ApiTimesAreUtc),
            Billable = source.Billable,
            Description = source.Description,
            ExternalKey = source.ExternalKey,
            Contract = source.Contract,
            Subcontract = source.Subcontract
        };
}

public class BreakInsertionSummary
{
    public int DaysScanned { get; set; }
    public int BreaksInserted { get; set; }

    /// <summary>Days that already carried our break but whose split was never made visible.</summary>
    public int DaysHealed { get; set; }

    /// <summary>Work records written back unchanged so their Revision refreshes for syncing clients.</summary>
    public int RecordsTouched { get; set; }

    /// <summary>Days whose break is in place but whose Revision refresh failed; retried next run.</summary>
    public int TouchFailed { get; set; }

    public int SkippedExistingBreak { get; set; }
    public int SkippedInProgress { get; set; }
    public int SkippedBelowThreshold { get; set; }
    public int SkippedHoursOnly { get; set; }
    public int SkippedNoSlot { get; set; }

    public int Failed { get; set; }
}
