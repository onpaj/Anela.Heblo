using Anela.Heblo.Persistence.Ga4;
using Anela.Heblo.Persistence.Ga4.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.GoogleAnalytics.Sync;

/// <summary>
/// Landing pages by day — backlog #8. Capped to the top N per day by sessions, because landing
/// page is the highest-cardinality dimension here and the target Postgres is a single vCore.
/// </summary>
public sealed class LandingPageSyncService : Ga4EntitySyncServiceBase
{
    private static readonly string[] Dimensions = ["date", "landingPage"];
    private static readonly string[] Metrics = ["sessions", "engagedSessions"];

    private readonly IGa4ReportClient _client;
    private readonly Ga4DbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public LandingPageSyncService(
        IGa4ReportClient client,
        IGa4SyncWatermarkRepository watermarkRepo,
        Ga4DbContext dbContext,
        IOptions<Ga4SyncOptions> options,
        TimeProvider timeProvider,
        ILogger<LandingPageSyncService> logger)
        : base(watermarkRepo, options.Value, timeProvider, logger)
    {
        _client = client;
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    protected override string EntityName => "landing_page_daily";

    protected override int? TopNPerDay => Options.TopLandingPagesPerDay;

    protected override async Task<ChunkOutcome> SyncChunkAsync(DateOnly start, DateOnly end, CancellationToken ct)
    {
        var report = await _client.RunReportAsync(
            new Ga4ReportRequest
            {
                Dimensions = Dimensions,
                Metrics = Metrics,
                StartDate = start,
                EndDate = end,
            },
            ct);

        var syncedAt = _timeProvider.GetUtcNow();

        // GA4 already returns one row per (date, landingPage), so this only orders and cuts.
        var incoming = report.Rows
            .Select(row => new LandingPageDaily
            {
                Date = Ga4ValueParser.ToDate(row.DimensionValues[0]),
                LandingPage = Ga4ValueParser.ToDimension(row.DimensionValues[1]),
                Sessions = Ga4ValueParser.ToLong(row.MetricValues[0]),
                EngagedSessions = Ga4ValueParser.ToLong(row.MetricValues[1]),
                SyncedAt = syncedAt,
            })
            .GroupBy(x => x.Date)
            .SelectMany(g => g.OrderByDescending(x => x.Sessions)
                              .ThenBy(x => x.LandingPage, StringComparer.Ordinal)
                              .Take(Options.TopLandingPagesPerDay))
            .ToList();

        var existing = await _dbContext.LandingPageDaily
            .Where(x => x.Date >= start && x.Date <= end)
            .ToListAsync(ct);

        var upserted = await Ga4ChunkUpsert.ReplaceRangeAsync(
            _dbContext,
            _dbContext.LandingPageDaily,
            incoming,
            existing,
            x => (x.Date, x.LandingPage),
            (current, fresh) =>
            {
                current.Sessions = fresh.Sessions;
                current.EngagedSessions = fresh.EngagedSessions;
                current.SyncedAt = fresh.SyncedAt;
            },
            Options.BatchSize,
            ct);

        return new ChunkOutcome(report.Rows.Count, upserted);
    }
}
