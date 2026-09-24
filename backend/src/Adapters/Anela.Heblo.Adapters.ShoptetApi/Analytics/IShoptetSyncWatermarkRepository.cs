using Anela.Heblo.Persistence.ShoptetOrders.Entities;

namespace Anela.Heblo.Adapters.ShoptetApi.Analytics;

public interface IShoptetSyncWatermarkRepository
{
    Task<ShoptetSyncState> GetOrCreateAsync(string entityName, CancellationToken ct = default);
    Task SaveAsync(ShoptetSyncState state, CancellationToken ct = default);
}
