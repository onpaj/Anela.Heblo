namespace Anela.Heblo.Adapters.ShoptetApi.Analytics;

public class ShoptetOrdersSyncOptions
{
    public const string ConfigurationKey = "ShoptetOrdersSync";

    /// <summary>
    /// Target database for the shoptet_raw schema. Empty leaves the whole stack unregistered, so an
    /// unconfigured environment is inert (same gate as AnalyticsDatabase:ConnectionString for flexi_raw).
    /// </summary>
    public string ConnectionString { get; set; } = "";

    public int MaxPoolSize { get; set; } = 10;

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 01:30 Prague. The 02:00–09:00 band is crowded with the existing daily imports and
    /// flexi-analytics-sync holds 03:00, so the incremental sync takes the quiet slot before them.
    /// </summary>
    public string CronExpression { get; set; } = "30 1 * * *";

    public string TimeZone { get; set; } = "Europe/Prague";

    /// <summary>
    /// The store's timezone. Order dates are bucketed by this, not by UTC, so a 23:30 Prague order
    /// lands in the day the owners would call it.
    /// </summary>
    public string StoreTimeZone { get; set; } = "Europe/Prague";

    /// <summary>Oldest creation date to backfill. The anela.cz store's first order is 2018-11.</summary>
    public string BackfillFrom { get; set; } = "2018-01-01";

    /// <summary>Creation-time window walked per backfill step. One month keeps each window under the 50-per-page paging depth Shoptet is comfortable with.</summary>
    public int BackfillWindowDays { get; set; } = 31;

    /// <summary>
    /// Wall-clock budget for one backfill invocation. The full history is ~97k detail calls at
    /// ~4 req/s ≈ 7 hours, so the backfill is designed to be resumed across several runs.
    /// </summary>
    public int BackfillMaxMinutesPerRun { get; set; } = 240;

    /// <summary>Orders persisted per SaveChanges. Keeps memory and transaction size bounded on the 1-vCore server.</summary>
    public int BatchSize { get; set; } = 200;

    /// <summary>
    /// Measured ceiling on the live store: ~4 req/s sequential is clean, ~16 req/s returns 429s.
    /// Left below 4 so a concurrent Heblo process sharing the token does not push it over.
    /// </summary>
    public double RequestsPerSecond { get; set; } = 3.0;

    public int MaxRetryAttempts { get; set; } = 5;

    public int RequestTimeoutSeconds { get; set; } = 60;

    /// <summary>Overall cancellation budget for one job execution.</summary>
    public int JobTimeoutSeconds { get; set; } = 18000;

    /// <summary>
    /// Safety margin subtracted from the watermark, mirroring LedgerSyncService's
    /// state.Watermark.Value.AddHours(-1). Absorbs clock skew between Shoptet and Heblo.
    /// </summary>
    public int WatermarkSafetyMarginHours { get; set; } = 2;

    public DateOnly GetBackfillFromDate() =>
        DateOnly.FromDateTime(
            DateTimeOffset.Parse(BackfillFrom, null, System.Globalization.DateTimeStyles.AssumeUniversal)
                          .UtcDateTime);
}
