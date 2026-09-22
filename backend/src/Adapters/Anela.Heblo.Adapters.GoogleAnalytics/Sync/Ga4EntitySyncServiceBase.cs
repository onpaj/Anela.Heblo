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
    /// Lets a month-grain table widen the window to whole calendar months. Asking GA4 for
    /// yearMonth over 15 August - 14 September returns half an August, which would then overwrite
    /// a complete August row.
    /// </summary>
    protected virtual (DateOnly Start, DateOnly End) AlignWindow(DateOnly start, DateOnly end) => (start, end);

    protected abstract Task<ChunkOutcome> SyncChunkAsync(DateOnly start, DateOnly end, CancellationToken ct);

    public async Task<Ga4SyncResult> SyncAsync(CancellationToken ct = default)
    {
        var state = await _watermarkRepo.GetOrCreateAsync(EntityName, ct);

        state.LastRunStartedAt = _timeProvider.GetUtcNow();
        state.LastRunStatus = "RUNNING";
        state.TopNPerDay = TopNPerDay;
        await _watermarkRepo.SaveAsync(state, ct);

        var totalFetched = 0;
        var totalUpserted = 0;

        try
        {
            // Inside the try: GetBackfillFromDate() throws on a malformed Ga4Sync:BackfillFrom,
            // and sync_state is where an operator looks to find out why a table stopped moving.
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

                var chunkEnd = Min(chunkStart.AddDays(ChunkDays - 1), end);
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

            state.LastRunStatus = "OK";
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
            state.LastRunStatus = "FAILED";
            state.LastRunFinishedAt = _timeProvider.GetUtcNow();
            state.LastRunRowsFetched = totalFetched;
            state.LastRunRowsUpserted = totalUpserted;
            state.LastErrorMessage = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;

            _logger.LogError(ex, "Ga4Sync.EntityFailed {EntityName}", EntityName);
        }

        await _watermarkRepo.SaveAsync(state, ct);
        return new Ga4SyncResult(EntityName, totalFetched, totalUpserted, state.LastRunStatus == "OK");
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
