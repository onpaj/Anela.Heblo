namespace Anela.Heblo.Application.Features.MarketingAds.Configuration;

public sealed class AdsChangeSyncOptions
{
    /// <summary>Hourly at :15, clear of the :00 jobs; Hangfire runs one worker, so this run must stay short.</summary>
    public string CronExpression { get; set; } = "15 * * * *";
    public string TimeZone { get; set; } = "Europe/Prague";
    public int RunTimeoutMinutes { get; set; } = 15;
    /// <summary>How far back the very first change-log pull reaches (capped by the platform's ChangeLogMaxAge).</summary>
    public int InitialLookbackDays { get; set; } = 7;
}
