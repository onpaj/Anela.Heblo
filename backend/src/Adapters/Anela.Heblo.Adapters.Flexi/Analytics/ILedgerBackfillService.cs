namespace Anela.Heblo.Adapters.Flexi.Analytics;

/// <summary>
/// One-off historical load of the general ledger, separate from the nightly incremental sync.
///
/// The nightly path (<see cref="IEntitySyncService"/>) pages FlexiBee's `lastUpdate gte` filter,
/// which is right for a small delta but wrong for the initial load: the whole ledger has been
/// touched since 2020, so the first run would page ~680k rows through a single ever-deepening
/// offset against a shared 1-vCore Postgres. This walks accounting-date month windows instead,
/// keeping every offset shallow and every window independently restartable.
/// </summary>
public interface ILedgerBackfillService
{
    /// <summary>
    /// Loads <c>flexi_raw.ledger_entry</c> month by month over the inclusive accounting-date range.
    /// Idempotent: re-running a window upserts the same rows.
    /// </summary>
    Task<SyncResult> BackfillAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
}
