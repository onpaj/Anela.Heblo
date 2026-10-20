using Anela.Heblo.Persistence.ShoptetOrders.Entities;

namespace Anela.Heblo.Adapters.ShoptetApi.Analytics;

public interface IShoptetOrderStore
{
    /// <summary>
    /// Inserts or updates the given orders and replaces their line items wholesale.
    /// Idempotent: re-running with the same input leaves exactly the same rows.
    /// </summary>
    Task<int> UpsertAsync(IReadOnlyList<ShoptetOrder> orders, CancellationToken ct = default);

    /// <summary>Removes orders (and their lines) that Shoptet reports as deleted.</summary>
    Task<int> DeleteAsync(IReadOnlyCollection<string> orderCodes, CancellationToken ct = default);
}
