using Anela.Heblo.Persistence.Ga4;
using Anela.Heblo.Persistence.Ga4.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.GoogleAnalytics.Sync;

/// <summary>Site traffic by day and default channel group — backlog #9, and the denominator for #7.</summary>
public sealed class TrafficSyncService : Ga4EntitySyncServiceBase
{
    private static readonly string[] Dimensions = ["date", "sessionDefaultChannelGroup"];
    private static readonly string[] Metrics = Ga4TrafficMetrics.All;

    private readonly IGa4ReportClient _client;
    private readonly Ga4DbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public TrafficSyncService(
        IGa4ReportClient client,
        IGa4SyncWatermarkRepository watermarkRepo,
        Ga4DbContext dbContext,
        IOptions<Ga4SyncOptions> options,
        TimeProvider timeProvider,
        ILogger<TrafficSyncService> logger)
        : base(watermarkRepo, options.Value, timeProvider, logger)
    {
        _client = client;
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    protected override string EntityName => "traffic_daily";

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
        var incoming = WithoutOtherBucket(report.Rows)
            .Select(row => new TrafficDaily
            {
                Date = Ga4ValueParser.ToDate(row.DimensionValues[0]),
                ChannelGroup = Ga4ValueParser.ToDimension(row.DimensionValues[1]),
                Sessions = Ga4ValueParser.ToLong(row.MetricValues[0]),
                TotalUsers = Ga4ValueParser.ToLong(row.MetricValues[1]),
                NewUsers = Ga4ValueParser.ToLong(row.MetricValues[2]),
                ScreenPageViews = Ga4ValueParser.ToLong(row.MetricValues[3]),
                EngagedSessions = Ga4ValueParser.ToLong(row.MetricValues[4]),
                UserEngagementSeconds = Ga4ValueParser.ToLong(row.MetricValues[5]),
                SyncedAt = syncedAt,
            })
            .ToList();

        var existing = await _dbContext.TrafficDaily
            .Where(x => x.Date >= start && x.Date <= end)
            .ToListAsync(ct);

        var upserted = await Ga4ChunkUpsert.ReplaceRangeAsync(
            _dbContext,
            _dbContext.TrafficDaily,
            incoming,
            existing,
            x => (x.Date, x.ChannelGroup),
            (current, fresh) =>
            {
                current.Sessions = fresh.Sessions;
                current.TotalUsers = fresh.TotalUsers;
                current.NewUsers = fresh.NewUsers;
                current.ScreenPageViews = fresh.ScreenPageViews;
                current.EngagedSessions = fresh.EngagedSessions;
                current.UserEngagementSeconds = fresh.UserEngagementSeconds;
                current.SyncedAt = fresh.SyncedAt;
            },
            Options.BatchSize,
            ct);

        return new ChunkOutcome(report.Rows.Count, upserted);
    }
}
