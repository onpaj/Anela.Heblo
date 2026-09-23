namespace Anela.Heblo.Persistence.Ga4.Entities;

/// <summary>
/// One row per calendar month, asked of GA4 at month grain.
///
/// It exists for exactly one reason: users are not additive over time. Summing
/// <c>traffic_total_daily.total_users</c> across August 2026 gives 29,194 against GA4's own
/// 21,746 for that month — a 34% overstatement, because a visitor who comes back on three days is
/// counted three times. GA4 de-duplicates over whatever period it is asked about, so the month has
/// to be asked for directly. This table stores that answer. Sessions and page views would sum correctly, but they are kept here too so a
/// report can read one row and have every headline figure agree with the GA4 UI.
/// </summary>
public class TrafficMonthly
{
    /// <summary>First day of the month, so the column joins to <c>date_trunc('month', ...)</c>.</summary>
    public DateOnly Month { get; set; }

    public long Sessions { get; set; }

    /// <summary>Distinct users in the month, de-duplicated by GA4 across the whole month.</summary>
    public long TotalUsers { get; set; }

    public long NewUsers { get; set; }
    public long ScreenPageViews { get; set; }
    public long EngagedSessions { get; set; }
    public long UserEngagementSeconds { get; set; }

    public DateTimeOffset SyncedAt { get; set; }
}
