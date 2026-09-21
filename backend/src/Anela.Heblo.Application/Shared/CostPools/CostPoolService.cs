using Anela.Heblo.Application.Common;
using Anela.Heblo.Application.Common.TimePeriods;
using Anela.Heblo.Domain.Accounting.CostPools;
using Anela.Heblo.Domain.Accounting.Ledger;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Shared.CostPools;

/// <summary>
/// Computes monthly spend totals per cost pool from the Flexi ledger.
///
/// One unfiltered pull on every prefix any pool uses, which would replace the
/// three department-filtered pulls the cost providers make today once that dedup
/// follow-up lands. Because that pull is wider than what any single pool counts
/// (50x counts only in M2), entries are bucketed through CostPoolDefinition.Resolve
/// rather than summed blindly - which is what keeps the M2 total here identical to
/// the margin engine's M2.
/// </summary>
public class CostPoolService : ICostPoolService
{
    private static readonly SemaphoreSlim RefreshLock = new(1, 1);

    private readonly ICostPoolCache _cache;
    private readonly ILedgerService _ledgerService;
    private readonly ILogger<CostPoolService> _logger;
    private readonly DataSourceOptions _options;

    public CostPoolService(
        ICostPoolCache cache,
        ILedgerService ledgerService,
        ILogger<CostPoolService> logger,
        IOptions<DataSourceOptions> options)
    {
        _cache = cache;
        _ledgerService = ledgerService;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<MonthlyCostPool>> GetMonthlyPoolsAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default)
    {
        if (from > to)
        {
            throw new ArgumentException($"'{nameof(from)}' must not be later than '{nameof(to)}'.", nameof(from));
        }

        var cacheData = await _cache.GetCachedDataAsync(ct);

        if (cacheData.Covers(from, to))
        {
            return FilterToRange(cacheData.Pools, from, to);
        }

        return await ComputeAsync(from, to, ct);
    }

    private static IReadOnlyList<MonthlyCostPool> FilterToRange(
        IReadOnlyList<MonthlyCostPool> pools,
        DateOnly from,
        DateOnly to)
    {
        var firstMonth = new DateTime(from.Year, from.Month, 1);
        var lastMonth = new DateTime(to.Year, to.Month, 1);

        return pools
            .Where(p => p.Month >= firstMonth && p.Month <= lastMonth)
            .OrderBy(p => p.Month)
            .ThenBy(p => p.Pool)
            .ToList();
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (!await RefreshLock.WaitAsync(0, ct))
        {
            _logger.LogInformation("CostPoolCache refresh already in progress, skipping");
            return;
        }

        try
        {
            _logger.LogInformation("Starting CostPoolCache refresh");

            var to = DateOnly.FromDateTime(DateTime.UtcNow);
            var from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-_options.ManufactureCostHistoryDays));

            var pools = await ComputeAsync(from, to, ct);

            // ComputeAsync widens the window to whole months, so Pools really does
            // cover them. Recording the raw from/to instead would make Covers() miss
            // on the boundary months and trigger a needless live ledger pull.
            var (rangeStart, rangeEnd) = MonthRange.ToWholeMonths(from, to);
            var cachedFrom = DateOnly.FromDateTime(rangeStart);
            var cachedTo = DateOnly.FromDateTime(rangeEnd);

            await _cache.SetCachedDataAsync(new CostPoolCacheData
            {
                Pools = pools,
                LastUpdated = DateTime.UtcNow,
                DataFrom = cachedFrom,
                DataTo = cachedTo,
                IsHydrated = true
            }, ct);

            _logger.LogInformation(
                "CostPoolCache refreshed successfully: {RowCount} monthly pool totals covering {From} to {To}",
                pools.Count, cachedFrom, cachedTo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh CostPoolCache");
            throw;
        }
        finally
        {
            RefreshLock.Release();
        }
    }

    private async Task<IReadOnlyList<MonthlyCostPool>> ComputeAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken ct)
    {
        var (rangeStart, rangeEnd) = MonthRange.ToWholeMonths(from, to);

        var ledgerItems = await _ledgerService.GetLedgerItems(
            rangeStart,
            rangeEnd,
            debitAccountPrefix: CostPoolDefinition.AccountPrefixes,
            creditAccountPrefix: null,
            department: null,
            cancellationToken: ct);

        LogOverheadDepartments(ledgerItems);

        var totals = ledgerItems
            .Select(item => (
                Item: item,
                Pool: CostPoolDefinition.Resolve(item.Department, item.DebitAccountNumber)))
            .Where(x => x.Pool.HasValue)
            .GroupBy(x => (
                Month: new DateTime(x.Item.Date.Year, x.Item.Date.Month, 1),
                Pool: x.Pool!.Value))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Item.Amount));

        return MonthRange.EnumerateMonths(rangeStart, rangeEnd)
            .SelectMany(month => CostPoolDefinition.All.Select(pool =>
                new MonthlyCostPool(
                    month,
                    pool,
                    totals.TryGetValue((month, pool), out var amount) ? amount : 0m)))
            .ToList();
    }

    /// <summary>
    /// M3 is a catch-all, so a miscoded entry silently becomes overhead.
    /// Logging the codes that landed there makes a new or wrong one visible.
    /// </summary>
    private void LogOverheadDepartments(IEnumerable<LedgerItem> ledgerItems)
    {
        var overheadDepartments = ledgerItems
            .Where(item => CostPoolDefinition.Resolve(item.Department, item.DebitAccountNumber) == CostPool.M3)
            .Select(item => string.IsNullOrWhiteSpace(item.Department) ? "(none)" : item.Department)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (overheadDepartments.Count == 0)
        {
            return;
        }

        _logger.LogInformation(
            "CostPool M3 absorbed spend from departments: {OverheadDepartments}",
            string.Join(", ", overheadDepartments));
    }
}
