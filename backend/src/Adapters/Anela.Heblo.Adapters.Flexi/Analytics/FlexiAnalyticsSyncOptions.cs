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

    public DateTime GetInitialBackfillDateTime() =>
        DateTime.Parse(InitialBackfillFrom, null, System.Globalization.DateTimeStyles.AssumeLocal).Date;
}
