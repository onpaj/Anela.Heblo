namespace Anela.Heblo.Adapters.GoogleAnalytics.Sync;

/// <summary>
/// The six traffic metrics, requested identically by traffic_daily, traffic_total_daily and
/// traffic_monthly. Shared rather than repeated once per service: the three read their results
/// positionally, so a metric added to one list and not the others does not fail — it silently
/// shifts every later column and writes the wrong number into the wrong field.
/// </summary>
internal static class Ga4TrafficMetrics
{
    public static readonly string[] All =
    [
        "sessions", "totalUsers", "newUsers", "screenPageViews", "engagedSessions", "userEngagementDuration",
    ];
}
