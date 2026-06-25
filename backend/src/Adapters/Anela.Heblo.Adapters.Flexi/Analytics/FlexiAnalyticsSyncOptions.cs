namespace Anela.Heblo.Adapters.Flexi.Analytics;

public class FlexiAnalyticsSyncOptions
{
    public const string ConfigurationKey = "FlexiAnalyticsSync";

    public bool Enabled { get; set; } = true;
    public string CronExpression { get; set; } = "0 3 * * *";
    public string TimeZone { get; set; } = "Europe/Prague";
    public int BatchSize { get; set; } = 500;
    public string InitialBackfillFrom { get; set; } = "2020-01-01";
    public int RequestTimeoutSeconds { get; set; } = 120;

    // Npgsql 6+ rejects DateTime with Kind != Utc on 'timestamptz' columns.
    // Parse via DateTimeOffset (which carries explicit offset) so .UtcDateTime
    // always returns Kind=Utc regardless of server local timezone.
    // Do NOT use DateTime.Parse(...).Date.ToUniversalTime(): .Date strips Kind back
    // to Unspecified, causing a timezone shift on Prague-TZ containers (#3243 regression).
    public DateTime GetInitialBackfillDateTime() =>
        DateTimeOffset.Parse(InitialBackfillFrom, null, System.Globalization.DateTimeStyles.AssumeUniversal)
                      .UtcDateTime;
}
