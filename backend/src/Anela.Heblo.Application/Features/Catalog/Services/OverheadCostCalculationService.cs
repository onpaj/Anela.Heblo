using Anela.Heblo.Application.Common;
using Anela.Heblo.Domain.Accounting.Ledger;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Catalog.Services;

public class OverheadCostCalculationService : IOverheadCostCalculationService
{
    private readonly ICatalogRepository _catalogRepository;
    private readonly ILedgerService _ledgerService;
    private readonly TimeProvider _timeProvider;
    private readonly IOptions<DataSourceOptions> _dataSourceOptions;
    private readonly ILogger<OverheadCostCalculationService> _logger;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    private Dictionary<string, List<OverheadCost>>? _cachedData;
    private bool _isLoaded;

    public OverheadCostCalculationService(
        ICatalogRepository catalogRepository,
        ILedgerService ledgerService,
        TimeProvider timeProvider,
        IOptions<DataSourceOptions> dataSourceOptions,
        ILogger<OverheadCostCalculationService> logger)
    {
        _catalogRepository = catalogRepository;
        _ledgerService = ledgerService;
        _timeProvider = timeProvider;
        _dataSourceOptions = dataSourceOptions;
        _logger = logger;
    }

    private async Task<Dictionary<string, List<OverheadCost>>> CalculateOverheadCostHistoryAsync(CancellationToken cancellationToken = default)
    {
        // Return cached data if available
        if (_isLoaded && _cachedData != null)
        {
            _logger.LogDebug("Returning cached overhead cost history data");
            return _cachedData;
        }

        try
        {
            return await CalculateOverheadCostHistoryInternalAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating overhead cost history");
            return new Dictionary<string, List<OverheadCost>>();
        }
    }

    private async Task<Dictionary<string, List<OverheadCost>>> CalculateOverheadCostHistoryInternalAsync(CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, List<OverheadCost>>();

        // Get date range for last 13 months, values from last months are not accurate yet
        var endDate = DateOnly.FromDateTime(_timeProvider.GetUtcNow().Date);
        var startDate = endDate.AddDays(-1 * _dataSourceOptions.Value.ManufactureCostHistoryDays);

        _logger.LogDebug("Calculating overhead cost history from {StartDate} to {EndDate}", startDate, endDate);

        // Get direct costs from C department
        var overheadCosts = await _ledgerService.GetDirectCosts(startDate, endDate, "C", cancellationToken) ?? new List<CostStatistics>();

        // Group direct costs by month
        var monthlyCosts = overheadCosts
            .GroupBy(c => new { c.Date.Year, c.Date.Month })
            .ToDictionary(
                g => $"{g.Key.Year:D4}-{g.Key.Month:D2}",
                g => g.Sum(c => c.Cost)
            );


        var startDateTime = startDate.ToDateTime(TimeOnly.MinValue);
        var endDateTime = endDate.ToDateTime(TimeOnly.MinValue);
        _logger.LogDebug("Found overhead costs for {MonthCount} months", monthlyCosts.Count);
        var products = await _catalogRepository.GetAllAsync(cancellationToken);
        // Get all sale history from products and group by month
        var allSalesHistory = products
            .SelectMany(p => p.SalesHistory.Select(m => new { Product = p, SaleRecord = m }))
            .Where(x => x.SaleRecord.Date >= startDateTime && x.SaleRecord.Date <= endDateTime)
            .ToList();

        if (!allSalesHistory.Any())
            return result; // Data not ready yet

        var monthlySaleData = allSalesHistory
            .GroupBy(x => new { x.SaleRecord.Date.Year, x.SaleRecord.Date.Month })
            .ToDictionary(
                g => $"{g.Key.Year:D4}-{g.Key.Month:D2}",
                g => g.ToList()
            );

        _logger.LogDebug("Found sales history for {MonthCount} months with {TotalRecords} records",
            monthlySaleData.Count, allSalesHistory.Count);

        // For each month, calculate weighted costs
        foreach (var monthKey in monthlyCosts.Keys)
        {
            if (!monthlySaleData.TryGetValue(monthKey, out var monthSales))
                continue;

            var monthTotalCost = monthlyCosts[monthKey];

            // Calculate total units sold this month across all products
            var totalUnitsSoldThisMonth = monthSales.Sum(x => x.SaleRecord.AmountTotal);

            if (totalUnitsSoldThisMonth <= 0)
                continue; // Skip months with no sales

            // Calculate cost per unit for this month
            var costPerUnit = monthTotalCost / (decimal)totalUnitsSoldThisMonth;

            _logger.LogDebug("Month {Month}: Total overhead cost {TotalCost}, Total units {TotalUnits}, Cost per unit {CostPerUnit}",
                monthKey, monthTotalCost, totalUnitsSoldThisMonth, costPerUnit);

            // Parse month date for the OverheadCost record
            var dateParts = monthKey.Split('-');
            var monthDate = new DateTime(int.Parse(dateParts[0]), int.Parse(dateParts[1]), 1);

            // Group sales by product for this month
            var productSalesThisMonth = monthSales
                .GroupBy(x => x.Product.ProductCode)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.SaleRecord.AmountTotal));

            // Calculate overhead cost for each product based on their share of total sales
            foreach (var productSale in productSalesThisMonth)
            {
                var productCode = productSale.Key;
                var productUnitsSold = productSale.Value;

                // Calculate this product's share of total costs (cost per unit * units sold)
                var productOverheadCost = costPerUnit * (decimal)productUnitsSold;

                // Initialize product list if needed
                if (!result.ContainsKey(productCode))
                {
                    result[productCode] = new List<OverheadCost>();
                }

                var overheadCost = new OverheadCost
                {
                    Date = monthDate,
                    TotalCost = productOverheadCost,
                    AmountSold = productUnitsSold,
                    UnitCost = costPerUnit
                };

                result[productCode].Add(overheadCost);

                _logger.LogDebug("Product {ProductCode}: {UnitsSold} units sold, allocated overhead cost {Cost}",
                    productCode, productUnitsSold, productOverheadCost);
            }
        }

        _logger.LogInformation("Calculated overhead cost history for {ProductCount} products", result.Count);
        return result;
    }

    public bool IsLoaded => _isLoaded;

    public async Task Reload()
    {
        await _cacheLock.WaitAsync();
        try
        {
            _logger.LogInformation("Reloading overhead cost calculation cache");

            // Calculate new data
            var newData = await CalculateOverheadCostHistoryInternalAsync();
            if (!newData.Any())
            {
                _logger.LogInformation("Overhead cost calculation data not ready yet");
                return;
            }
            // Update cache atomically
            _cachedData = newData;
            _isLoaded = true;

            _logger.LogInformation("Overhead cost calculation cache reloaded with {ProductCount} products", newData.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reload overhead cost calculation cache");
            throw;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    public async Task<Dictionary<string, List<MonthlyCost>>> GetCostsAsync(
        List<string>? products,
        DateOnly? dateFrom = null,
        DateOnly? dateTo = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var now = DateOnly.FromDateTime(_timeProvider.GetUtcNow().Date);
            var endDate = dateTo ?? now;
            var startDate = dateFrom ?? endDate.AddMonths(-12);

            // Get overhead cost history using existing service
            var overheadCostHistory = await CalculateOverheadCostHistoryAsync(cancellationToken);

            var result = new Dictionary<string, List<MonthlyCost>>();

            foreach (var product in products ?? overheadCostHistory.Keys.ToList())
            {
                var monthlyCosts = new List<MonthlyCost>();

                if (overheadCostHistory.TryGetValue(product, out var costHistory))
                {
                    // Filter by date range, group by month and sum costs
                    var monthlyGroups = costHistory
                        .Where(c => DateOnly.FromDateTime(c.Date) >= startDate && DateOnly.FromDateTime(c.Date) <= endDate)
                        .GroupBy(c => new { c.Date.Year, c.Date.Month })
                        .Select(s => new
                        {
                            s.Key.Year,
                            s.Key.Month,
                            TotalCost = s.Sum(x => x.TotalCost),
                            UnitCost = s.Average(x => x.UnitCost)
                        })
                        .OrderBy(g => g.Year).ThenBy(g => g.Month);

                    foreach (var monthGroup in monthlyGroups)
                    {
                        // Use first day of month for consistency
                        var monthStart = new DateTime(monthGroup.Year, monthGroup.Month, 1);
                        monthlyCosts.Add(new MonthlyCost(monthStart, monthGroup.UnitCost));
                    }
                }

                // If we have some costs, add to result
                if (monthlyCosts.Count > 0)
                {
                    result[product] = monthlyCosts;
                }
            }

            _logger.LogDebug("Calculated overhead costs for {ProductCount} products", result.Count);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating overhead costs");
            throw;
        }
    }
}
