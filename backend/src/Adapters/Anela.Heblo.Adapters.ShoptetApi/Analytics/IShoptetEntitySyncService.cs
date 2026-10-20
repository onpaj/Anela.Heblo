namespace Anela.Heblo.Adapters.ShoptetApi.Analytics;

public interface IShoptetEntitySyncService
{
    string EntityName { get; }
    Task<ShoptetSyncResult> SyncAsync(CancellationToken ct = default);
}

public record ShoptetSyncResult(int RowsFetched, int RowsUpserted, bool IsSuccess, bool IsComplete);
