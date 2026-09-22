using Anela.Heblo.Persistence.Ga4;
using Anela.Heblo.Persistence.Ga4.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.GoogleAnalytics.Sync;

/// <summary>
/// Page views by day — backlog #37 (most-read articles). Optionally restricted to a set of path
/// prefixes inside the Data API, then capped to the top N per day by views.
/// </summary>
public sealed class PageSyncService : Ga4EntitySyncServiceBase
{
    private static readonly string[] Dimensions = ["date", "pagePath", "pageTitle"];
    private static readonly string[] Metrics = ["screenPageViews", "sessions"];

    private readonly IGa4ReportClient _client;
    private readonly Ga4DbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public PageSyncService(
        IGa4ReportClient client,
        IGa4SyncWatermarkRepository watermarkRepo,
        Ga4DbContext dbContext,
        IOptions<Ga4SyncOptions> options,
        TimeProvider timeProvider,
        ILogger<PageSyncService> logger)
        : base(watermarkRepo, options.Value, timeProvider, logger)
    {
        _client = client;
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    protected override string EntityName => "page_daily";

    protected override int? TopNPerDay => Options.TopPagesPerDay;

    protected override async Task<ChunkOutcome> SyncChunkAsync(DateOnly start, DateOnly end, CancellationToken ct)
    {
        var report = await _client.RunReportAsync(
            new Ga4ReportRequest
            {
                Dimensions = Dimensions,
                Metrics = Metrics,
                StartDate = start,
                EndDate = end,
                FilterDimension = "pagePath",
                FilterBeginsWithAny = Options.PagePathPrefixes,
            },
            ct);

        var syncedAt = _timeProvider.GetUtcNow();

        // pageTitle is in the report only so the ranking is readable; the same path can carry
        // several titles over time, so the rows collapse to the path and keep the busiest title.
        var incoming = WithoutOtherBucket(report.Rows)
            .Select(row => new
            {
                Date = Ga4ValueParser.ToDate(row.DimensionValues[0]),
                PagePath = Ga4ValueParser.ToDimension(row.DimensionValues[1]),
                PageTitle = row.DimensionValues[2],
                ScreenPageViews = Ga4ValueParser.ToLong(row.MetricValues[0]),
                Sessions = Ga4ValueParser.ToLong(row.MetricValues[1]),
            })
            .GroupBy(x => (x.Date, x.PagePath))
            .Select(g => new PageDaily
            {
                Date = g.Key.Date,
                PagePath = g.Key.PagePath,
                PageTitle = g.OrderByDescending(x => x.ScreenPageViews).First().PageTitle,
                ScreenPageViews = g.Sum(x => x.ScreenPageViews),
                Sessions = g.Sum(x => x.Sessions),
                SyncedAt = syncedAt,
            })
            .GroupBy(x => x.Date)
            .SelectMany(g => g.OrderByDescending(x => x.ScreenPageViews)
                              .ThenBy(x => x.PagePath, StringComparer.Ordinal)
                              .Take(Options.TopPagesPerDay))
            .ToList();

        var existing = await _dbContext.PageDaily
            .Where(x => x.Date >= start && x.Date <= end)
            .ToListAsync(ct);

        var upserted = await Ga4ChunkUpsert.ReplaceRangeAsync(
            _dbContext,
            _dbContext.PageDaily,
            incoming,
            existing,
            x => (x.Date, x.PagePath),
            (current, fresh) =>
            {
                current.PageTitle = fresh.PageTitle;
                current.ScreenPageViews = fresh.ScreenPageViews;
                current.Sessions = fresh.Sessions;
                current.SyncedAt = fresh.SyncedAt;
            },
            Options.BatchSize,
            ct);

        return new ChunkOutcome(report.Rows.Count, upserted);
    }
}
