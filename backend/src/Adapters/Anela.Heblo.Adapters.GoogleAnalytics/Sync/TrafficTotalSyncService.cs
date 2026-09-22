using Anela.Heblo.Persistence.Ga4;
using Anela.Heblo.Persistence.Ga4.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.GoogleAnalytics.Sync;

/// <summary>
/// The property's undimensioned daily totals — backlog #9, and the only figures here that match
/// what the GA4 UI shows. Same report as <see cref="TrafficSyncService"/> minus the channel
/// dimension, because GA4 totals cannot be recovered by summing a breakdown.
/// </summary>
public sealed class TrafficTotalSyncService : Ga4EntitySyncServiceBase
{
    private static readonly string[] Dimensions = ["date"];
    private static readonly string[] Metrics =
    [
        "sessions", "totalUsers", "newUsers", "screenPageViews", "engagedSessions", "userEngagementDuration",
    ];

    private readonly IGa4ReportClient _client;
    private readonly Ga4DbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public TrafficTotalSyncService(
        IGa4ReportClient client,
        IGa4SyncWatermarkRepository watermarkRepo,
        Ga4DbContext dbContext,
        IOptions<Ga4SyncOptions> options,
        TimeProvider timeProvider,
        ILogger<TrafficTotalSyncService> logger)
        : base(watermarkRepo, options.Value, timeProvider, logger)
    {
        _client = client;
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    protected override string EntityName => "traffic_total_daily";

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
        var incoming = report.Rows
            .Select(row => new TrafficTotalDaily
            {
                Date = Ga4ValueParser.ToDate(row.DimensionValues[0]),
                Sessions = Ga4ValueParser.ToLong(row.MetricValues[0]),
                TotalUsers = Ga4ValueParser.ToLong(row.MetricValues[1]),
                NewUsers = Ga4ValueParser.ToLong(row.MetricValues[2]),
                ScreenPageViews = Ga4ValueParser.ToLong(row.MetricValues[3]),
                EngagedSessions = Ga4ValueParser.ToLong(row.MetricValues[4]),
                UserEngagementSeconds = Ga4ValueParser.ToLong(row.MetricValues[5]),
                SyncedAt = syncedAt,
            })
            .ToList();

        var existing = await _dbContext.TrafficTotalDaily
            .Where(x => x.Date >= start && x.Date <= end)
            .ToListAsync(ct);

        var upserted = await Ga4ChunkUpsert.ReplaceRangeAsync(
            _dbContext,
            _dbContext.TrafficTotalDaily,
            incoming,
            existing,
            x => x.Date,
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
