using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Adapters.ShoptetApi.Analytics;

/// <summary>
/// Resolves the store's wall-clock zone, which decides both an order's calendar day and the
/// backfill's window boundaries. Shared so the two cannot disagree: they once resolved it
/// separately, and the backfill's copy fell back to UTC without logging, quietly shifting every
/// window boundary by an hour or two.
/// </summary>
internal static class ShoptetTimeZone
{
    public static TimeZoneInfo Resolve(string id, ILogger logger)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            logger.LogWarning(ex,
                "ShoptetOrdersSync.UnknownStoreTimeZone id={TimeZoneId} — falling back to UTC", id);
            return TimeZoneInfo.Utc;
        }
    }
}
