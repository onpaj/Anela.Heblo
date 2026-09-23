namespace Anela.Heblo.Persistence.Ga4.Entities;

/// <summary>
/// One row per day, with no dimension breakdown at all — the property's own headline figures.
///
/// This exists because GA4 numbers do not decompose cleanly. Sessions broken down by channel sum
/// to 0.0–3.8% above the ungrouped total (measured June–August 2026), because GA4 computes each
/// requested dimension combination separately rather than partitioning one exact figure.
///
/// Note the limit of this table: its rows are still per-day, so summing <c>total_users</c> over a
/// month counts a returning visitor once per day and overstates by about a third. Monthly
/// headline figures come from <see cref="TrafficMonthly"/>, not from here.
/// </summary>
public class TrafficTotalDaily
{
    public DateOnly Date { get; set; }

    public long Sessions { get; set; }
    public long TotalUsers { get; set; }
    public long NewUsers { get; set; }
    public long ScreenPageViews { get; set; }
    public long EngagedSessions { get; set; }
    public long UserEngagementSeconds { get; set; }

    public DateTimeOffset SyncedAt { get; set; }
}
