namespace Anela.Heblo.Adapters.Flexi.Analytics;

public interface IEntitySyncService
{
    Task<SyncResult> SyncAsync(CancellationToken ct = default);
}
