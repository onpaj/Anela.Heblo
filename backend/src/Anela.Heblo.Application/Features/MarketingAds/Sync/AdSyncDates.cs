namespace Anela.Heblo.Application.Features.MarketingAds.Sync;

/// <summary>Calendar dates are the ad account's local dates — that is how every platform reports a day.</summary>
public static class AdSyncDates
{
    public const string FallbackTimeZoneId = "Europe/Prague";

    public static DateOnly Yesterday(DateTimeOffset now, string? timeZoneId) => Today(now, timeZoneId).AddDays(-1);

    public static DateOnly Today(DateTimeOffset now, string? timeZoneId) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, Resolve(timeZoneId)).Date);

    public static IReadOnlyList<DateOnly> FactWindow(DateOnly yesterday, int lookbackDays) =>
        Enumerable.Range(0, lookbackDays + 1).Select(i => yesterday.AddDays(i - lookbackDays)).ToList();

    /// <summary>Splits an inclusive date range into calendar-month pieces (the first and last may be partial).</summary>
    public static IReadOnlyList<(DateOnly From, DateOnly To)> MonthChunks(DateOnly from, DateOnly to)
    {
        var chunks = new List<(DateOnly, DateOnly)>();
        for (var start = from; start <= to;)
        {
            var monthEnd = new DateOnly(start.Year, start.Month, 1).AddMonths(1).AddDays(-1);
            var end = monthEnd < to ? monthEnd : to;
            chunks.Add((start, end));
            start = end.AddDays(1);
        }
        return chunks;
    }

    public static IReadOnlyList<DateOnly> Days(DateOnly from, DateOnly to) =>
        Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).Select(from.AddDays).ToList();

    private static TimeZoneInfo Resolve(string? timeZoneId)
    {
        if (!string.IsNullOrWhiteSpace(timeZoneId) && TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var zone))
            return zone;
        return TimeZoneInfo.FindSystemTimeZoneById(FallbackTimeZoneId);
    }
}
