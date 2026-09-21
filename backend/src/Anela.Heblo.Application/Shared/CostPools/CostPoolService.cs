using Anela.Heblo.Application.Common;
using Anela.Heblo.Domain.Accounting.CostPools;
using Anela.Heblo.Domain.Accounting.Ledger;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Shared.CostPools;

/// <summary>
/// Computes monthly spend totals per cost pool from the Flexi ledger.
///
/// One unfiltered pull on accounts 51+52, which would replace the three
/// department-filtered pulls the cost providers make today once that dedup
/// follow-up lands. Amounts are summed exactly as
/// LedgerService.GetCosts does - trusting the server-side debit-prefix filter
/// rather than re-checking client-side - which is what keeps the M2 total here
/// identical to the margin engine's M2.
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

            await _cache.SetCachedDataAsync(new CostPoolCacheData
            {
                Pools = pools,
                LastUpdated = DateTime.UtcNow,
                DataFrom = from,
                DataTo = to,
                IsHydrated = true
            }, ct);

            _logger.LogInformation(
                "CostPoolCache refreshed successfully: {RowCount} monthly pool totals covering {From} to {To}",
                pools.Count, from, to);
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
        var (rangeStart, rangeEnd) = ToWholeMonthRange(from, to);

        var ledgerItems = await _ledgerService.GetLedgerItems(
            rangeStart,
            rangeEnd,
            debitAccountPrefix: CostPoolDefinition.AccountPrefixes,
            creditAccountPrefix: null,
            department: null,
            cancellationToken: ct);

        LogOverheadDepartments(ledgerItems);

        var totals = ledgerItems
            .GroupBy(item => (
                Month: new DateTime(item.Date.Year, item.Date.Month, 1),
                Pool: CostPoolDefinition.Resolve(item.Department)))
            .ToDictionary(g => g.Key, g => g.Sum(item => item.Amount));

        return GenerateMonths(rangeStart, rangeEnd)
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
            .Where(item => CostPoolDefinition.Resolve(item.Department) == CostPool.M3)
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

    /// <summary>
    /// Expands a date range to whole calendar months, matching the window logic
    /// the Catalog cost providers use in their GetDateRange helpers.
    /// </summary>
    private static (DateTime start, DateTime end) ToWholeMonthRange(DateOnly from, DateOnly to)
    {
        var start = new DateTime(from.Year, from.Month, 1);
        var end = new DateTime(
            to.Year, to.Month, DateTime.DaysInMonth(to.Year, to.Month), 23, 59, 59);

        return (start, end);
    }

    private static IEnumerable<DateTime> GenerateMonths(DateTime start, DateTime end)
    {
        var current = new DateTime(start.Year, start.Month, 1);
        var last = new DateTime(end.Year, end.Month, 1);

        while (current <= last)
        {
            yield return current;
            current = current.AddMonths(1);
        }
    }
}
