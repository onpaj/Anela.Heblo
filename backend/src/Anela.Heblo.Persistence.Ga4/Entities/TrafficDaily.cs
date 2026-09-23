namespace Anela.Heblo.Persistence.Ga4.Entities;

/// <summary>
/// One row per (date, default channel group) from the GA4 Data API.
///
/// Every stored metric is additive, so any roll-up (month, quarter, all channels) is a plain SUM.
/// Bounce rate is deliberately NOT stored: it is a ratio, so storing it per row invites an
/// AVG() that silently weights a 3-session day the same as a 3000-session day. It is derived
/// exactly as 1 - engaged_sessions / sessions wherever it is needed (see the v_ views).
/// </summary>
public class TrafficDaily
{
    public DateOnly Date { get; set; }

    /// <summary>GA4 dimension <c>sessionDefaultChannelGroup</c> (Organic Search, Direct, Paid Social, ...).</summary>
    public string ChannelGroup { get; set; } = "";

    public long Sessions { get; set; }

    /// <summary>
    /// Users who reached the site through this channel on this day. Correct per row. Summing it
    /// across channels within one day happens to land within ~1% of the day's real total, but
    /// summing it across days does not: a visitor returning on three days is counted three times,
    /// which overstates a month by about a third. For any figure spanning more than one day use
    /// <see cref="TrafficMonthly.TotalUsers"/>.
    /// </summary>
    public long TotalUsers { get; set; }
    public long NewUsers { get; set; }
    public long ScreenPageViews { get; set; }
    public long EngagedSessions { get; set; }

    /// <summary>GA4 metric <c>userEngagementDuration</c>, in seconds. Additive; divide by sessions for an average.</summary>
    public long UserEngagementSeconds { get; set; }

    public DateTimeOffset SyncedAt { get; set; }
}
