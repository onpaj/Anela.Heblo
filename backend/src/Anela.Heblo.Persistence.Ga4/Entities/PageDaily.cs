namespace Anela.Heblo.Persistence.Ga4.Entities;

/// <summary>
/// One row per (date, page path) from the GA4 Data API, capped to the top N pages per day by
/// views (<c>Ga4Sync:TopPagesPerDay</c>) and optionally restricted to a path
/// prefix (<c>Ga4Sync:PagePathPrefixes</c>) so the article ranking for #37 does
/// not drag the entire catalogue in with it.
/// </summary>
public class PageDaily
{
    public DateOnly Date { get; set; }

    /// <summary>GA4 dimension <c>pagePath</c>.</summary>
    public string PagePath { get; set; } = "";

    /// <summary>GA4 dimension <c>pageTitle</c> of the most-viewed variant for that path and day.</summary>
    public string? PageTitle { get; set; }

    public long ScreenPageViews { get; set; }
    public long Sessions { get; set; }

    public DateTimeOffset SyncedAt { get; set; }
}
