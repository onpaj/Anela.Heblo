namespace Anela.Heblo.Application.Features.MarketingAds.Sync;

/// <summary>Calendar dates are the ad account's local dates — that is how every platform reports a day.</summary>
public static class AdSyncDates
{
    public const string FallbackTimeZoneId = "Europe/Prague";

    public static DateOnly Yesterday(DateTimeOffset now, string? timeZoneId)
    {
        var local = TimeZoneInfo.ConvertTime(now, Resolve(timeZoneId));
        return DateOnly.FromDateTime(local.Date).AddDays(-1);
    }

    public static IReadOnlyList<DateOnly> FactWindow(DateOnly yesterday, int lookbackDays) =>
        Enumerable.Range(0, lookbackDays + 1).Select(i => yesterday.AddDays(i - lookbackDays)).ToList();

    private static TimeZoneInfo Resolve(string? timeZoneId)
    {
        if (!string.IsNullOrWhiteSpace(timeZoneId) && TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var zone))
            return zone;
        return TimeZoneInfo.FindSystemTimeZoneById(FallbackTimeZoneId);
    }
}
