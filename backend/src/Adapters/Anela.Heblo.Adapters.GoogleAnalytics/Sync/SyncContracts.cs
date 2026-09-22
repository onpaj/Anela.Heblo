using Anela.Heblo.Persistence.Ga4.Entities;

namespace Anela.Heblo.Adapters.GoogleAnalytics.Sync;

public interface IGa4EntitySyncService
{
    Task<Ga4SyncResult> SyncAsync(CancellationToken ct = default);
}

public sealed record Ga4SyncResult(string EntityName, int RowsFetched, int RowsUpserted, bool IsSuccess);

/// <summary>Rows read from GA4 and rows written, for one date chunk.</summary>
public sealed record ChunkOutcome(int RowsFetched, int RowsUpserted);

public interface IGa4SyncWatermarkRepository
{
    Task<SyncState> GetOrCreateAsync(string entityName, CancellationToken ct = default);
    Task SaveAsync(SyncState state, CancellationToken ct = default);

    /// <summary>
    /// Drops everything the shared context is still tracking after a failed SaveChangesAsync and
    /// returns a freshly loaded sync_state row to record the failure on. Without it the failed
    /// write's entities are replayed by the next SaveChangesAsync — the bookkeeping write itself,
    /// or the next table's watermark — so a run recorded as FAILED still commits rows.
    /// </summary>
    Task<SyncState> DiscardPendingChangesAsync(string entityName, CancellationToken ct = default);
}
