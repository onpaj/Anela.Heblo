namespace Anela.Heblo.Domain.Accounting.CostPools;

/// <summary>
/// Monthly company spend totals (accounts 51, 52) split into cost pools.
///
/// Reads the same ILedgerService path the margin engine uses, so the M2 total
/// here is the same number SalesCostProvider divides by sold pieces.
/// </summary>
public interface ICostPoolService
{
    /// <summary>
    /// Monthly totals for every pool covering the requested range.
    ///
    /// The range is expanded to whole calendar months. Every month in range is
    /// present for every pool, with Amount = 0 where there was no spend, so
    /// callers never have to distinguish "no data" from "no spend".
    ///
    /// Served from cache when the cached window covers the range, otherwise
    /// computed live. Ledger failures propagate - this never returns a
    /// silently-zeroed result.
    /// </summary>
    Task<IReadOnlyList<MonthlyCostPool>> GetMonthlyPoolsAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default);

    /// <summary>
    /// Recomputes the default window into the cache. Registered as a background
    /// refresh task. Skips (does not queue) when a refresh is already running.
    /// </summary>
    Task RefreshAsync(CancellationToken ct = default);
}
