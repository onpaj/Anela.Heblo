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
    // AssumeLocal produced Kind=Local, which caused ArgumentException in the nightly
    // Hangfire sync job. AssumeUniversal + ToUniversalTime() guarantees Kind=Utc.
    public DateTime GetInitialBackfillDateTime() =>
        DateTime.Parse(InitialBackfillFrom, null, System.Globalization.DateTimeStyles.AssumeUniversal)
                .Date
                .ToUniversalTime();
}
