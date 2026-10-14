namespace Anela.Heblo.Application.Features.MarketingAds.Configuration;

public sealed class AdsDailySyncOptions
{
    /// <summary>05:30 Prague — after the platforms close yesterday, before people start work.</summary>
    public string CronExpression { get; set; } = "30 5 * * *";
    public string TimeZone { get; set; } = "Europe/Prague";
    public int RunTimeoutMinutes { get; set; } = 45;
}
