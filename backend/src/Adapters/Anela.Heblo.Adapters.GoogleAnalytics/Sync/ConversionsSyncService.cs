using Anela.Heblo.Persistence.Ga4;
using Anela.Heblo.Persistence.Ga4.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.GoogleAnalytics.Sync;

/// <summary>
/// GA4's own e-commerce figures by day and channel — the numerator for #7. See the caveat on
/// <c>v_monthly_conversion</c>: these are purchase events, not the ERP's orders.
/// </summary>
public sealed class ConversionsSyncService : Ga4EntitySyncServiceBase
{
    private static readonly string[] Dimensions = ["date", "sessionDefaultChannelGroup"];
    private static readonly string[] Metrics = ["transactions", "purchaseRevenue"];

    private readonly IGa4ReportClient _client;
    private readonly Ga4DbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public ConversionsSyncService(
        IGa4ReportClient client,
        IGa4SyncWatermarkRepository watermarkRepo,
        Ga4DbContext dbContext,
        IOptions<Ga4SyncOptions> options,
        TimeProvider timeProvider,
        ILogger<ConversionsSyncService> logger)
        : base(watermarkRepo, options.Value, timeProvider, logger)
    {
        _client = client;
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    protected override string EntityName => "conversions_daily";

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
            .Select(row => new ConversionsDaily
            {
                Date = Ga4ValueParser.ToDate(row.DimensionValues[0]),
                ChannelGroup = Ga4ValueParser.ToDimension(row.DimensionValues[1]),
                Transactions = Ga4ValueParser.ToLong(row.MetricValues[0]),
                PurchaseRevenue = Ga4ValueParser.ToDecimal(row.MetricValues[1]),
                SyncedAt = syncedAt,
            })
            .ToList();

        var existing = await _dbContext.ConversionsDaily
            .Where(x => x.Date >= start && x.Date <= end)
            .ToListAsync(ct);

        var upserted = await Ga4ChunkUpsert.ReplaceRangeAsync(
            _dbContext,
            _dbContext.ConversionsDaily,
            incoming,
            existing,
            x => (x.Date, x.ChannelGroup),
            (current, fresh) =>
            {
                current.Transactions = fresh.Transactions;
                current.PurchaseRevenue = fresh.PurchaseRevenue;
                current.SyncedAt = fresh.SyncedAt;
            },
            Options.BatchSize,
            ct);

        return new ChunkOutcome(report.Rows.Count, upserted);
    }
}
