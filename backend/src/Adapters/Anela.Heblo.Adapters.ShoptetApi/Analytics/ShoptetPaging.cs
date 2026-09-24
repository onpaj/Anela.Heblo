using Anela.Heblo.Adapters.ShoptetApi.Analytics.Model;

namespace Anela.Heblo.Adapters.ShoptetApi.Analytics;

/// <summary>
/// The stop condition shared by every paged read in this sync.
///
/// Guarding it in one place matters because of what the callers do next: the backfill persists an
/// advanced cursor after each window and the incremental sync stamps a new watermark, both of
/// which are irreversible. A page series that ends early therefore does not read as an error, it
/// reads as "that window held fewer orders than it did" — and nothing ever revisits it. So a
/// truncated read has to throw rather than return short.
/// </summary>
internal static class ShoptetPaging
{
    /// <summary>
    /// True when <paramref name="page"/> was the last page of the series. Throws when the response
    /// cannot be shown to be complete.
    /// </summary>
    public static bool IsLastPage(
        ShoptetPaginatorDto? paginator, int itemsOnPage, int page, string description)
    {
        if (paginator == null)
        {
            throw new ShoptetOrderSyncException(
                $"Shoptet returned no paginator for {description} page {page}; the read cannot be "
                + "shown to be complete, so the cursor must not advance past it.");
        }

        if (page >= paginator.PageCount)
            return true;

        // More pages were promised but this one came back empty — a truncated read, not the end.
        if (itemsOnPage == 0)
        {
            throw new ShoptetOrderSyncException(
                $"Shoptet returned an empty {description} page {page} of {paginator.PageCount} "
                + $"(totalCount={paginator.TotalCount}); refusing to treat a truncated read as the "
                + "end of the series.");
        }

        return false;
    }

    /// <summary>
    /// Verifies a completed page series returned as many rows as the paginator promised. Extra rows
    /// are tolerated — the store keeps changing underneath a multi-page read — but missing ones mean
    /// the read was short and whatever was skipped would never be revisited.
    /// </summary>
    public static void EnsureComplete(
        ShoptetPaginatorDto? paginator, int collected, string description)
    {
        if (paginator == null || collected >= paginator.TotalCount)
            return;

        throw new ShoptetOrderSyncException(
            $"Shoptet reported totalCount={paginator.TotalCount} for {description} but only "
            + $"{collected} rows were read; refusing to advance past an incomplete read.");
    }
}
