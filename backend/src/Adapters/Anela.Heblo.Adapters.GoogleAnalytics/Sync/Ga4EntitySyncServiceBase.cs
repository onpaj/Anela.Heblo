using Anela.Heblo.Persistence.Ga4.Entities;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Adapters.GoogleAnalytics.Sync;

/// <summary>
/// The part every GA4 table's sync shares: work out the window, walk it in chunks, advance the
/// watermark after each chunk, and record the outcome on sync_state.
///
/// Advancing per chunk is what makes a multi-year backfill resumable — a run that dies in month
/// 14 resumes at month 14, not at the start. The subclass only has to fetch and upsert one chunk.
/// </summary>
public abstract class Ga4EntitySyncServiceBase : IGa4EntitySyncService
{
    private const string StatusRunning = "RUNNING";
    private const string StatusOk = "OK";
    private const string StatusFailed = "FAILED";

    /// <summary>
    /// What GA4 puts in a dimension it bucketed because the report exceeded its cardinality limit.
    /// </summary>
    private const string OtherBucket = "(other)";

    private readonly IGa4SyncWatermarkRepository _watermarkRepo;
    private readonly Ga4SyncOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;

    protected Ga4EntitySyncServiceBase(
        IGa4SyncWatermarkRepository watermarkRepo,
        Ga4SyncOptions options,
        TimeProvider timeProvider,
        ILogger logger)
    {
        _watermarkRepo = watermarkRepo;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected Ga4SyncOptions Options => _options;

    /// <summary>Table name, and the sync_state key.</summary>
    protected abstract string EntityName { get; }

    /// <summary>The per-day cap this table applies, recorded on sync_state. Null when uncapped.</summary>
    protected virtual int? TopNPerDay => null;

    /// <summary>Days per Data API request. Month-grain tables widen this so a month is never split.</summary>
    protected virtual int ChunkDays => _options.ChunkDays;

    /// <summary>
    /// A misconfigured ChunkDays of 0 or less would make chunkEnd precede chunkStart, so GA4 gets
    /// an inverted range and the table fails every night with an opaque API error. Clamping makes
    /// the misconfiguration slow rather than silent.
    /// </summary>
    private int EffectiveChunkDays => Math.Max(1, ChunkDays);

    /// <summary>
    /// Defensive only: drops a row GA4 bucketed so hard that even its date reads "(other)", which
    /// cannot be stored — the date is part of every primary key here. Because a chunk's request is
    /// deterministic, letting that throw would wedge the table on the same window every night
    /// rather than failing once.
    ///
    /// This has NOT been observed on property 392098710. The 39-month backfill hit the cardinality
    /// limit exactly once (page_daily, Feb 2024) and GA4 bucketed only the high-cardinality
    /// dimensions, leaving the date intact. Such a row is therefore KEPT: its metrics are real
    /// traffic, and dropping it would silently under-report the day. It reaches the table as a
    /// page_path of "(other)", which the ranking views exclude.
    ///
    /// Anything else unparseable is still a hard failure — that is a real format change, not a
    /// documented bucket.
    /// </summary>
    protected IReadOnlyList<Ga4Row> WithoutOtherBucket(IReadOnlyList<Ga4Row> rows)
    {
        var kept = rows
            .Where(row => row.DimensionValues.Count == 0 || row.DimensionValues[0] != OtherBucket)
            .ToList();

        if (kept.Count != rows.Count)
        {
            _logger.LogWarning(
                "Ga4Sync.UndatedBucketRowsSkipped {EntityName} skipped={Skipped} of {Total} — GA4 bucketed the date dimension itself, so these rows cannot be attributed to a day.",
                EntityName, rows.Count - kept.Count, rows.Count);
        }

        return kept;
    }

    /// <summary>
    /// Lets a month-grain table widen the window to whole calendar months. Asking GA4 for
    /// yearMonth over 15 August - 14 September returns half an August, which would then overwrite
    /// a complete August row.
    /// </summary>
    protected virtual (DateOnly Start, DateOnly End) AlignWindow(DateOnly start, DateOnly end) => (start, end);

    protected abstract Task<ChunkOutcome> SyncChunkAsync(DateOnly start, DateOnly end, CancellationToken ct);

    public async Task<Ga4SyncResult> SyncAsync(CancellationToken ct = default)
    {
        var totalFetched = 0;
        var totalUpserted = 0;
        SyncState? state = null;

        try
        {
            // Inside the try: sync_state is the one surface an operator reads to find out why a
            // table stopped moving, so a failure to even bootstrap it has to be recorded, not
            // thrown past the bookkeeping below.
            state = await _watermarkRepo.GetOrCreateAsync(EntityName, ct);

            state.LastRunStartedAt = _timeProvider.GetUtcNow();
            state.LastRunStatus = StatusRunning;
            state.TopNPerDay = TopNPerDay;
            await _watermarkRepo.SaveAsync(state, ct);

            // GetBackfillFromDate() throws on a malformed Ga4Sync:BackfillFrom.
            var (start, end) = AlignWindow(StartDateFor(state), Yesterday());

            _logger.LogInformation(
                "Ga4Sync.EntityStarted {EntityName} watermark={Watermark} start={Start} end={End}",
                EntityName, state.WatermarkDate, start, end);

            if (start > end)
            {
                _logger.LogInformation(
                    "Ga4Sync.EntityUpToDate {EntityName} watermark={Watermark} — nothing to pull.",
                    EntityName, state.WatermarkDate);
            }

            var chunkStart = start;
            while (chunkStart <= end)
            {
                ct.ThrowIfCancellationRequested();

                var chunkEnd = Min(chunkStart.AddDays(EffectiveChunkDays - 1), end);
                var outcome = await SyncChunkAsync(chunkStart, chunkEnd, ct);

                totalFetched += outcome.RowsFetched;
                totalUpserted += outcome.RowsUpserted;

                _logger.LogInformation(
                    "Ga4Sync.ChunkCompleted {EntityName} start={Start} end={End} rowsFetched={RowsFetched} rowsUpserted={RowsUpserted}",
                    EntityName, chunkStart, chunkEnd, outcome.RowsFetched, outcome.RowsUpserted);

                // Persist progress before moving on, so an interrupted backfill resumes here.
                state.WatermarkDate = chunkEnd;
                await _watermarkRepo.SaveAsync(state, ct);

                chunkStart = chunkEnd.AddDays(1);
            }

            state.LastRunStatus = StatusOk;
            state.LastRunFinishedAt = _timeProvider.GetUtcNow();
            state.LastRunRowsFetched = totalFetched;
            state.LastRunRowsUpserted = totalUpserted;
            state.LastErrorMessage = null;

            _logger.LogInformation(
                "Ga4Sync.EntityCompleted {EntityName} rowsFetched={RowsFetched} rowsUpserted={RowsUpserted}",
                EntityName, totalFetched, totalUpserted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ga4Sync.EntityFailed {EntityName}", EntityName);

            // A failed SaveChangesAsync leaves its entities tracked, and every sync service in
            // the run shares one scoped Ga4DbContext. Without this, the next SaveChangesAsync —
            // including the bookkeeping write below, and the next table's watermark — replays
            // them, so a run recorded as FAILED still commits rows.
            state = await _watermarkRepo.DiscardPendingChangesAsync(EntityName, CancellationToken.None);

            state.LastRunStatus = StatusFailed;
            state.LastRunFinishedAt = _timeProvider.GetUtcNow();
            state.LastRunRowsFetched = totalFetched;
            state.LastRunRowsUpserted = totalUpserted;
            state.LastErrorMessage = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
        }

        // CancellationToken.None, not ct. Ga4SyncJob cancels the run after RequestTimeoutSeconds,
        // and saving the FAILED status on the already-cancelled token threw immediately — leaving
        // sync_state saying RUNNING forever with no reason recorded, which is precisely the case
        // an operator is trying to diagnose.
        await _watermarkRepo.SaveAsync(state, CancellationToken.None);
        return new Ga4SyncResult(EntityName, totalFetched, totalUpserted, state.LastRunStatus == StatusOk);
    }

    /// <summary>
    /// First run backfills from configuration. Every later run rewinds
    /// <see cref="Ga4SyncOptions.TrailingReprocessDays"/> behind the watermark, because GA4 keeps
    /// revising a day for about 48 hours after it is collected — a "yesterday only" pull would
    /// permanently under-report.
    /// </summary>
    private DateOnly StartDateFor(SyncState state)
    {
        if (state.WatermarkDate is not { } watermark)
            return _options.GetBackfillFromDate();

        var rewound = watermark.AddDays(-_options.TrailingReprocessDays);
        var backfillFrom = _options.GetBackfillFromDate();
        return rewound < backfillFrom ? backfillFrom : rewound;
    }

    /// <summary>
    /// Yesterday in the property's reporting timezone. Today is deliberately excluded: it is
    /// always partial, and the trailing re-pull picks it up tomorrow anyway.
    /// </summary>
    private DateOnly Yesterday()
    {
        var timeZone = ResolveTimeZone();
        var localNow = TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), timeZone);
        return DateOnly.FromDateTime(localNow.Date).AddDays(-1);
    }

    private TimeZoneInfo ResolveTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(_options.TimeZone);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            _logger.LogWarning(
                "Ga4Sync.UnknownTimeZone {TimeZone} — falling back to UTC for the 'yesterday' boundary.",
                _options.TimeZone);
            return TimeZoneInfo.Utc;
        }
    }

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;
}
