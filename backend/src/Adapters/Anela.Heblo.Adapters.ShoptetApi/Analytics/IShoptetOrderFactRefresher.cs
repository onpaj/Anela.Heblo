namespace Anela.Heblo.Adapters.ShoptetApi.Analytics;

/// <summary>
/// Refreshes the shoptet_raw.order_fact materialised view that every read view sits on.
/// </summary>
public interface IShoptetOrderFactRefresher
{
    /// <summary>
    /// Returns false when the view does not exist yet — the schema migration can be applied without
    /// the views script having been run, and a sync must not fail because of that.
    /// </summary>
    Task<bool> RefreshAsync(CancellationToken ct = default);
}
