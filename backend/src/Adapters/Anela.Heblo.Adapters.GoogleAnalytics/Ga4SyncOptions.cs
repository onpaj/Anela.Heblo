namespace Anela.Heblo.Adapters.GoogleAnalytics;

public class Ga4SyncOptions
{
    public const string ConfigurationKey = "Ga4Sync";

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 04:10 Prague. flexi-analytics-sync holds 0 3 * * *; this sits clear of it and of the
    /// nightly catalogue jobs, and late enough that GA4 has settled the previous day.
    /// </summary>
    public string CronExpression { get; set; } = "10 4 * * *";

    public string TimeZone { get; set; } = "Europe/Prague";

    /// <summary>
    /// Earliest date the backfill will ask for. The GA4 Data API refuses nothing for old dates —
    /// it simply returns no rows — so setting this before the property started collecting costs
    /// a few empty requests, not an error.
    /// </summary>
    public string BackfillFrom { get; set; } = "2024-01-01";

    /// <summary>
    /// How many days before the watermark each run re-pulls and upserts.
    ///
    /// GA4 keeps reprocessing a day's data for roughly 48 hours after it is collected, so a run
    /// that only ever asked for "yesterday" would bake in whatever partial figure happened to be
    /// visible at 04:10 and never correct it. Seven days is comfortably past that window.
    /// </summary>
    public int TrailingReprocessDays { get; set; } = 7;

    /// <summary>Days per Data API request. One request covering a month beats thirty covering a day each.</summary>
    public int ChunkDays { get; set; } = 31;

    /// <summary>Rows per Data API page. The API's own ceiling is 250,000.</summary>
    public int PageSize { get; set; } = 100_000;

    /// <summary>Top landing pages kept per day, by sessions. Recorded on the sync_state row.</summary>
    public int TopLandingPagesPerDay { get; set; } = 100;

    /// <summary>Top pages kept per day, by views. Recorded on the sync_state row.</summary>
    public int TopPagesPerDay { get; set; } = 100;

    /// <summary>
    /// When non-empty, page_daily only keeps paths beginning with one of these (filtered inside
    /// the Data API, so the payload stays small). Empty means keep every path.
    /// </summary>
    public string[] PagePathPrefixes { get; set; } = Array.Empty<string>();

    /// <summary>Rows per SaveChanges. Keeps the single-vCore Postgres server out of long transactions.</summary>
    public int BatchSize { get; set; } = 500;

    /// <summary>Pause between Data API requests, to stay well inside the per-hour token quota during a backfill.</summary>
    public int ThrottleMilliseconds { get; set; } = 250;

    public int RequestTimeoutSeconds { get; set; } = 1800;

    /// <summary>
    /// Parses <see cref="BackfillFrom"/>. Uses DateOnly rather than DateTime throughout: the GA4
    /// Data API speaks in property-local calendar dates, and dragging a Kind/offset through that
    /// only creates the timezone bugs the Flexi sync had to fix.
    /// </summary>
    public DateOnly GetBackfillFromDate() =>
        DateOnly.ParseExact(BackfillFrom, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}
