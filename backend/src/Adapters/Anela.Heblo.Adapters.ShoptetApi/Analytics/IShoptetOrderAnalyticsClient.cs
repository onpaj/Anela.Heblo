using Anela.Heblo.Adapters.ShoptetApi.Analytics.Model;

namespace Anela.Heblo.Adapters.ShoptetApi.Analytics;

/// <summary>
/// Read-only Shoptet order reader used by the shoptet_raw mirror. Every method is a GET —
/// the store is live (there is no sandbox for the production catalogue of orders), so this
/// client never issues POST, PATCH or DELETE.
/// </summary>
public interface IShoptetOrderAnalyticsClient
{
    /// <summary>
    /// Enumerates order codes created inside [createdFrom, createdTo). Paging is stable because
    /// creation time never changes, which is what makes the backfill resumable.
    /// </summary>
    Task<ShoptetOrderCodeListData> ListCodesByCreationTimeAsync(
        DateTimeOffset createdFrom, DateTimeOffset createdTo, int page, CancellationToken ct = default);

    /// <summary>Enumerates order codes changed at or after <paramref name="changedFrom"/>.</summary>
    Task<ShoptetOrderCodeListData> ListCodesByChangeTimeAsync(
        DateTimeOffset changedFrom, int page, CancellationToken ct = default);

    /// <summary>
    /// The 30-day edit/delete log. Unlike the order list this also reports deletions, which is the
    /// only way to learn that a mirrored order no longer exists.
    /// </summary>
    Task<ShoptetOrderChangeListData> ListChangesAsync(
        DateTimeOffset changedFrom, int page, int itemsPerPage, CancellationToken ct = default);

    /// <summary>Full order detail including items[] and completion[]. Null when the order is gone (404).</summary>
    Task<ShoptetOrderDetailDto?> GetOrderAsync(string code, CancellationToken ct = default);

    /// <summary>The verbatim detail JSON, stored in raw_payload so a missed field is a SQL query away.</summary>
    Task<(ShoptetOrderDetailDto? Order, string RawJson)> GetOrderWithRawAsync(string code, CancellationToken ct = default);
}
