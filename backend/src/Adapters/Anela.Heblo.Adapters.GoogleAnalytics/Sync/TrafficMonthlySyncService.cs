using System.Globalization;
using Anela.Heblo.Persistence.Ga4;
using Anela.Heblo.Persistence.Ga4.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.GoogleAnalytics.Sync;

/// <summary>
/// Month-grain totals, so #9 can report users that GA4 itself would recognise. Asked at month
/// grain rather than rolled up from days, because GA4 de-duplicates users within whatever period
/// it is asked about, and no amount of summing daily rows recovers that.
/// </summary>
public sealed class TrafficMonthlySyncService : Ga4EntitySyncServiceBase
{
    private static readonly string[] Dimensions = ["yearMonth"];
    private static readonly string[] Metrics =
    [
        "sessions", "totalUsers", "newUsers", "screenPageViews", "engagedSessions", "userEngagementDuration",
    ];

    private readonly IGa4ReportClient _client;
    private readonly Ga4DbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public TrafficMonthlySyncService(
        IGa4ReportClient client,
        IGa4SyncWatermarkRepository watermarkRepo,
        Ga4DbContext dbContext,
        IOptions<Ga4SyncOptions> options,
        TimeProvider timeProvider,
        ILogger<TrafficMonthlySyncService> logger)
        : base(watermarkRepo, options.Value, timeProvider, logger)
    {
        _client = client;
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    protected override string EntityName => "traffic_monthly";

    /// <summary>Wide enough that the aligned window is always a single request — it returns one row per month.</summary>
    protected override int ChunkDays => 4000;

    /// <summary>
    /// Whole calendar months only. The end is left at yesterday rather than pushed to the end of
    /// the month: the current month is legitimately partial, and re-pulling it every day is what
    /// keeps it current.
    /// </summary>
    protected override (DateOnly Start, DateOnly End) AlignWindow(DateOnly start, DateOnly end) =>
        AlignToWholeMonths(start, end);

    internal static (DateOnly Start, DateOnly End) AlignToWholeMonths(DateOnly start, DateOnly end) =>
        (new DateOnly(start.Year, start.Month, 1), end);

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
            .Select(row => new TrafficMonthly
            {
                Month = ParseYearMonth(row.DimensionValues[0]),
                Sessions = Ga4ValueParser.ToLong(row.MetricValues[0]),
                TotalUsers = Ga4ValueParser.ToLong(row.MetricValues[1]),
                NewUsers = Ga4ValueParser.ToLong(row.MetricValues[2]),
                ScreenPageViews = Ga4ValueParser.ToLong(row.MetricValues[3]),
                EngagedSessions = Ga4ValueParser.ToLong(row.MetricValues[4]),
                UserEngagementSeconds = Ga4ValueParser.ToLong(row.MetricValues[5]),
                SyncedAt = syncedAt,
            })
            .ToList();

        var firstMonth = new DateOnly(start.Year, start.Month, 1);
        var existing = await _dbContext.TrafficMonthly
            .Where(x => x.Month >= firstMonth && x.Month <= end)
            .ToListAsync(ct);

        var upserted = await Ga4ChunkUpsert.ReplaceRangeAsync(
            _dbContext,
            _dbContext.TrafficMonthly,
            incoming,
            existing,
            x => x.Month,
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

    /// <summary>GA4's <c>yearMonth</c> dimension is "YYYYMM"; it is stored as the first of that month.</summary>
    internal static DateOnly ParseYearMonth(string value) =>
        DateOnly.TryParseExact(value, "yyyyMM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : throw new FormatException($"GA4 returned an unparseable 'yearMonth' dimension value: '{value}'.");
}
