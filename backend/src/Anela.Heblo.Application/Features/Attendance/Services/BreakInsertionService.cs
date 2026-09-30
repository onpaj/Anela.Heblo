using Anela.Heblo.Domain.Features.Attendance;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Attendance.Services;

public class BreakInsertionService
{
    private readonly ILogetoClient _client;
    private readonly IOptions<BreakInsertionOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly IBreakInsertionRunGate _runGate;
    private readonly ILogger<BreakInsertionService> _logger;

    public BreakInsertionService(
        ILogetoClient client,
        IOptions<BreakInsertionOptions> options,
        TimeProvider timeProvider,
        IBreakInsertionRunGate runGate,
        ILogger<BreakInsertionService> logger)
    {
        _client = client;
        _options = options;
        _timeProvider = timeProvider;
        _runGate = runGate;
        _logger = logger;
    }

    /// <summary>Runs the nightly walk over the configured rolling window.</summary>
    public Task<BreakInsertionSummary> RunAsync(CancellationToken cancellationToken) =>
        RunAsync(fromDaysAgo: null, toDaysAgo: null, cancellationToken);

    /// <summary>
    /// Runs the walk over an explicit window, expressed as whole days before today. Used for a
    /// deliberate sweep over history — the work is idempotent, so re-covering days already handled
    /// is free, and the window can be moved back in steps to reach days the nightly job never sees.
    /// </summary>
    /// <param name="fromDaysAgo">Oldest day to scan, in days before today. Null uses the configured nightly lookback.</param>
    /// <param name="toDaysAgo">Newest day to scan, in days before today. Null means today.</param>
    /// <exception cref="BreakInsertionAlreadyRunningException">Another walk is in flight.</exception>
    public async Task<BreakInsertionSummary> RunAsync(
        int? fromDaysAgo, int? toDaysAgo, CancellationToken cancellationToken)
    {
        // A manual sweep racing the nightly job is the realistic case; see IBreakInsertionRunGate.
        if (!_runGate.TryEnter())
        {
            throw new BreakInsertionAlreadyRunningException();
        }

        try
        {
            return await RunCoreAsync(fromDaysAgo, toDaysAgo, cancellationToken);
        }
        finally
        {
            _runGate.Exit();
        }
    }

    private async Task<BreakInsertionSummary> RunCoreAsync(
        int? fromDaysAgo, int? toDaysAgo, CancellationToken cancellationToken)
    {
        var options = _options.Value;
        var summary = new BreakInsertionSummary();

        var pragueNow = TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), LogetoTimeConverter.PragueTimeZone);
        var today = DateOnly.FromDateTime(pragueNow.Date);
        var fromDays = Math.Max(fromDaysAgo ?? options.LookbackDays, 0);
        var toDays = Math.Max(toDaysAgo ?? 0, 0);
        var windowStart = today.AddDays(-fromDays);
        var from = windowStart < options.StartDate ? options.StartDate : windowStart;
        var to = today.AddDays(-toDays);

        if (from > to)
        {
            _logger.LogWarning(
                "Break insertion window is empty: computed from {From} (StartDate {StartDate}) is after to {To}. Nothing to do.",
                from, options.StartDate, to);
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

        var entries = await _client.GetTimeTrackingAsync(from, to, cancellationToken);

        foreach (var person in people)
        {
            var days = entries
                .Where(e => e.Person == person.Guid && e.Date >= from && e.Date <= to)
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
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    summary.Failed++;
                    _logger.LogError(ex,
                        "Failed to process day {Date} for person {PersonGuid}", day.Key, person.Guid);
                }
            }
        }

        _logger.LogInformation(
            "Break insertion finished: {Scanned} days scanned, {Inserted} breaks inserted, " +
            "{Healed} days healed, {Recreated} records recreated ({RecreateFailed} days failed to " +
            "recreate), {ExistingBreak} already fine, {InProgress} in progress, {BelowThreshold} below " +
            "threshold, {HoursOnly} hours-only, {NoSlot} no slot, {Failed} failed",
            summary.DaysScanned, summary.BreaksInserted, summary.DaysHealed, summary.RecordsRecreated,
            summary.RecreateFailed, summary.SkippedExistingBreak, summary.SkippedInProgress,
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
            await HealExistingBreakAsync(
                person, date, dayEntries, existingBreaks, typeByActivity, options, summary, cancellationToken);
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

        // A record another integration owns cannot be recreated around the break, so a break over it
        // would only sit beside it and never reduce the worked time.
        var foreign = windowed.FirstOrDefault(e =>
            !IsRecreatable(e, person.Guid, date)
            && Overlaps(ToSlot(e, options), slot));
        if (foreign is not null)
        {
            summary.SkippedNoSlot++;
            _logger.LogWarning(
                "Not inserting a break for person {PersonGuid} on {Date}: it would cover work record " +
                "{EntryGuid} carrying ExternalKey '{ExternalKey}', which this job does not own — fix manually in Logeto.",
                person.Guid, date, foreign.Guid, foreign.ExternalKey);
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

        // merge=false: the break goes in beside the work record, which is then recreated around it
        // below. Letting Logeto split it (merge=true) rewrites the record in place, and phones never
        // pick an API rewrite up — no API write moves TimestampChanged, which is what they sync on.
        await _client.CreateTimeEntryAsync(request, merge: false, cancellationToken);
        summary.BreaksInserted++;

        _logger.LogInformation(
            "Inserted {Minutes}-minute break {From}–{To} for person {PersonGuid} on {Date}",
            options.BreakDurationMinutes, request.From, request.To, person.Guid, date);

        // The break is already written, so a failure from here on must not be reported as a failed
        // insert: the day merely still overlaps, and the healing path finishes it next run.
        try
        {
            await RecreateWorkAroundBreakAsync(
                person, date, dayEntries, slot, typeByActivity, options, summary, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            summary.RecreateFailed++;
            _logger.LogError(ex,
                "Break was inserted for person {PersonGuid} on {Date}, but recreating the work records " +
                "around it failed. The day overlaps until a later run finishes it.",
                person.Guid, date);
        }
    }

    /// <summary>
    /// Finishes a day that already carries a break. Only the break this job created is considered:
    /// a break a worker entered themselves was never cut into their work by us, and their records
    /// are not ours to rewrite.
    /// </summary>
    private async Task HealExistingBreakAsync(
        LogetoPerson person,
        DateOnly date,
        IReadOnlyList<LogetoTimeEntry> dayEntries,
        IReadOnlyList<LogetoTimeEntry> existingBreaks,
        IReadOnlyDictionary<Guid, string> typeByActivity,
        BreakInsertionOptions options,
        BreakInsertionSummary summary,
        CancellationToken cancellationToken)
    {
        var ownBreak = existingBreaks.FirstOrDefault(e =>
            e.ExternalKey == AutoBreakExternalKey(person.Guid, date) && e.From.HasValue && e.To.HasValue);

        if (ownBreak is null)
        {
            summary.SkippedExistingBreak++;
            return;
        }

        var breakSlot = new TimeSlot(
            LogetoTimeConverter.ToPragueLocal(ownBreak.From!.Value, options.ApiTimesAreUtc),
            LogetoTimeConverter.ToPragueLocal(ownBreak.To!.Value, options.ApiTimesAreUtc));

        try
        {
            var recreated = await RecreateWorkAroundBreakAsync(
                person, date, dayEntries, breakSlot, typeByActivity, options, summary, cancellationToken);

            // Day-level buckets stay mutually exclusive so they reconcile against DaysScanned;
            // RecordsRecreated counts records and is deliberately a different unit.
            if (recreated > 0)
            {
                summary.DaysHealed++;
            }
            else
            {
                summary.SkippedExistingBreak++;
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            summary.RecreateFailed++;
            _logger.LogError(ex,
                "Failed to recreate the work records around the break on {Date} for person {PersonGuid}. " +
                "The day is retried on the next run.",
                date, person.Guid);
        }
    }

    /// <summary>
    /// Replaces every work record the break cuts into or borders with brand-new records covering
    /// the same time minus the break, then deletes the originals. New records carry a fresh
    /// TimestampChanged, so phones download them; an API rewrite of an existing record never does.
    /// <para>
    /// All creates happen before any delete, so a failure can never lose worked time — at worst
    /// the day briefly overlaps. Each replacement carries a deterministic ExternalKey, so a rerun
    /// skips what already exists, and replaced records are never picked up again. Records carrying
    /// any ExternalKey (ours or another integration's) are left alone.
    /// </para>
    /// </summary>
    /// <returns>The number of original records replaced.</returns>
    private async Task<int> RecreateWorkAroundBreakAsync(
        LogetoPerson person,
        DateOnly date,
        IReadOnlyList<LogetoTimeEntry> dayEntries,
        TimeSlot breakSlot,
        IReadOnlyDictionary<Guid, string> typeByActivity,
        BreakInsertionOptions options,
        BreakInsertionSummary summary,
        CancellationToken cancellationToken)
    {
        var existingKeys = dayEntries
            .Select(e => e.ExternalKey)
            .Where(k => !string.IsNullOrEmpty(k))
            .ToHashSet();

        // A worker's record is recreated when the break cuts into it or borders it — bordering is
        // what an older merge=true split left behind. One of our own replacements is recreated only
        // when a break cuts into it; one that merely borders our break is already done.
        var originals = dayEntries
            .Where(e => typeByActivity.GetValueOrDefault(e.Activity) == LogetoActivityTypes.Work
                && e.From.HasValue && e.To.HasValue && e.To > e.From
                && IsRecreatable(e, person.Guid, date))
            .Select(e => (Entry: e, Slot: ToSlot(e, options)))
            .Where(o => string.IsNullOrEmpty(o.Entry.ExternalKey)
                ? o.Slot.Start <= breakSlot.End && o.Slot.End >= breakSlot.Start
                : Overlaps(o.Slot, breakSlot))
            .OrderBy(o => o.Slot.Start)
            .ToList();

        foreach (var (entry, slot) in originals)
        {
            var pieces = PiecesOutsideBreak(slot, breakSlot).ToList();
            if (pieces.Count == 0)
            {
                _logger.LogWarning(
                    "Work record {EntryGuid} ({From}–{To}) for person {PersonGuid} on {Date} lies entirely " +
                    "inside the break and is deleted without a replacement",
                    entry.Guid, slot.Start, slot.End, person.Guid, date);
            }

            foreach (var piece in pieces)
            {
                var key = WorkPieceExternalKey(person.Guid, date, piece.Start, entry.Guid);
                if (existingKeys.Contains(key))
                {
                    continue;
                }

                await _client.CreateTimeEntryAsync(
                    BuildReplacementRequest(entry, piece, key, options), merge: false, cancellationToken);
            }
        }

        var replaced = 0;

        foreach (var (entry, slot) in originals)
        {
            await _client.DeleteTimeEntryAsync(entry.Guid, cancellationToken);

            // Counted at the delete itself: if a later one throws, those already done still show.
            replaced++;
            summary.RecordsRecreated++;

            _logger.LogInformation(
                "Recreated work record {EntryGuid} ({From}–{To}) for person {PersonGuid} on {Date} " +
                "around the break {BreakFrom}–{BreakTo}",
                entry.Guid, slot.Start, slot.End, person.Guid, date, breakSlot.Start, breakSlot.End);
        }

        return replaced;
    }

    /// <summary>The parts of a work record that lie before and after the break.</summary>
    private static IEnumerable<TimeSlot> PiecesOutsideBreak(TimeSlot work, TimeSlot breakSlot)
    {
        if (work.Start < breakSlot.Start)
        {
            yield return new TimeSlot(work.Start, work.End < breakSlot.Start ? work.End : breakSlot.Start);
        }

        if (work.End > breakSlot.End)
        {
            yield return new TimeSlot(work.Start > breakSlot.End ? work.Start : breakSlot.End, work.End);
        }
    }

    /// <summary>
    /// The key this job stamps on every break it creates. It is what tells our own breaks apart from
    /// the ones workers enter themselves, which must never be touched.
    /// </summary>
    private static string AutoBreakExternalKey(Guid personGuid, DateOnly date) =>
        $"autobreak-{personGuid}-{date:yyyy-MM-dd}";

    /// <summary>
    /// The key of a work record recreated around our break. It is derived from the piece's start and
    /// the record it replaces, so a rerun can tell which replacements already exist and two records
    /// whose pieces start at the same minute never collide. Logeto caps ExternalKey at 100
    /// characters; this is 71.
    /// </summary>
    private static string WorkPieceExternalKey(Guid personGuid, DateOnly date, DateTime start, Guid source) =>
        $"{AutoBreakExternalKey(personGuid, date)}-{start:HHmm}-{source.ToString("N")[..8]}";

    /// <summary>A worker's own record (no key) or a replacement this job created earlier.</summary>
    private static bool IsRecreatable(LogetoTimeEntry entry, Guid personGuid, DateOnly date) =>
        string.IsNullOrEmpty(entry.ExternalKey)
        || entry.ExternalKey.StartsWith($"{AutoBreakExternalKey(personGuid, date)}-", StringComparison.Ordinal);

    private static bool Overlaps(TimeSlot a, TimeSlot b) => a.Start < b.End && a.End > b.Start;

    private static TimeSlot ToSlot(LogetoTimeEntry entry, BreakInsertionOptions options) => new(
        LogetoTimeConverter.ToPragueLocal(entry.From!.Value, options.ApiTimesAreUtc),
        LogetoTimeConverter.ToPragueLocal(entry.To!.Value, options.ApiTimesAreUtc));

    private static LogetoTimeEntryRequest BuildReplacementRequest(
        LogetoTimeEntry source, TimeSlot piece, string externalKey, BreakInsertionOptions options) => new()
        {
            Person = source.Person,
            Activity = source.Activity,
            Date = source.Date,
            From = LogetoTimeConverter.ToApiTime(piece.Start, options.ApiTimesAreUtc),
            To = LogetoTimeConverter.ToApiTime(piece.End, options.ApiTimesAreUtc),
            Billable = source.Billable,
            Description = source.Description,
            ExternalKey = externalKey,
            Contract = source.Contract,
            Subcontract = source.Subcontract
        };
}

public class BreakInsertionSummary
{
    public int DaysScanned { get; set; }
    public int BreaksInserted { get; set; }

    /// <summary>Days that already carried our break but whose work records were not yet recreated.</summary>
    public int DaysHealed { get; set; }

    /// <summary>Original work records replaced by new ones around the break.</summary>
    public int RecordsRecreated { get; set; }

    /// <summary>Days whose break is in place but whose work records could not be recreated; retried next run.</summary>
    public int RecreateFailed { get; set; }

    public int SkippedExistingBreak { get; set; }
    public int SkippedInProgress { get; set; }
    public int SkippedBelowThreshold { get; set; }
    public int SkippedHoursOnly { get; set; }
    public int SkippedNoSlot { get; set; }

    public int Failed { get; set; }
}
