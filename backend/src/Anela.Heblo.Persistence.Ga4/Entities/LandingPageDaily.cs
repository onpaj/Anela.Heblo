namespace Anela.Heblo.Persistence.Ga4.Entities;

/// <summary>
/// One row per (date, landing page) from the GA4 Data API, capped to the top N landing pages
/// per day by sessions (<c>Ga4Sync:TopLandingPagesPerDay</c>). The cap is recorded
/// on the sync_state row so a later reader can tell a truncated day from a complete one.
/// </summary>
public class LandingPageDaily
{
    public DateOnly Date { get; set; }

    /// <summary>
    /// GA4 dimension <c>landingPage</c> — the path of a session's first pageview, without the
    /// query string. Deliberately not <c>landingPagePlusQueryString</c>: that splits one page
    /// across every campaign parameter it was ever reached with, which pushes the genuinely
    /// busiest pages out of the ranking.
    /// </summary>
    public string LandingPage { get; set; } = "";

    /// <summary>
    /// Sessions that started on this page. This is the count the GA4 Data API offers — it has no
    /// <c>entrances</c> metric (that was Universal Analytics), and since a session has exactly one
    /// landing page, sessions is the same measure under a different name.
    /// </summary>
    public long Sessions { get; set; }

    public long EngagedSessions { get; set; }

    public DateTimeOffset SyncedAt { get; set; }
}
