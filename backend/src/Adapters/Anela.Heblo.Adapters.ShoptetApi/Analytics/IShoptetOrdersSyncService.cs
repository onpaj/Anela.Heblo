namespace Anela.Heblo.Adapters.ShoptetApi.Analytics;

public interface IShoptetOrdersSyncService
{
    Task<ShoptetOrdersSyncReport> SyncAsync(CancellationToken ct = default);
}

public record ShoptetOrdersSyncReport(
    int TotalFetched,
    int TotalUpserted,
    bool BackfillCompleted,
    bool IsFullSuccess);
