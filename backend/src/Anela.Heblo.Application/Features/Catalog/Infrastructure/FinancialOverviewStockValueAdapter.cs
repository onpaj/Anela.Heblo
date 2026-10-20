using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.FinancialOverview;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

/// <summary>
/// Cross-module adapter: Catalog implements FinancialOverview's IStockValueService using Catalog-owned ERP clients.
/// Stock is valued at the warehouse valuation of each stock-to-date row (<see cref="ErpStock.Price"/>),
/// not at the ceník purchase price, which for manufactured items is the cost of the next manufacture.
/// </summary>
internal sealed class FinancialOverviewStockValueAdapter : IStockValueService
{
    private readonly IErpStockClient _stockClient;
    private readonly ILogger<FinancialOverviewStockValueAdapter> _logger;

    // Warehouse IDs from FlexiStockClient
    private const int MaterialWarehouseId = 5;    // MATERIAL
    private const int SemiProductsWarehouseId = 20; // POLOTOVARY
    private const int ProductsWarehouseId = 4;    // ZBOZI

    public FinancialOverviewStockValueAdapter(
        IErpStockClient stockClient,
        ILogger<FinancialOverviewStockValueAdapter> logger)
    {
        _stockClient = stockClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MonthlyStockChange>> GetStockValueChangesAsync(
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Calculating stock value changes from {StartDate} to {EndDate}",
            startDate, endDate);

        try
        {
            var monthlyChanges = new List<MonthlyStockChange>();

            // Process each month in the date range
            var currentDate = new DateTime(startDate.Year, startDate.Month, 1);
            var endMonth = new DateTime(endDate.Year, endDate.Month, 1);

            while (currentDate <= endMonth)
            {
                _logger.LogDebug("Processing stock changes for {Year}/{Month}", currentDate.Year, currentDate.Month);

                var monthlyChange = await CalculateMonthlyStockChangeAsync(
                    currentDate, cancellationToken);

                monthlyChanges.Add(monthlyChange);

                // Move to next month
                currentDate = currentDate.AddMonths(1);
            }

            _logger.LogInformation("Successfully calculated stock value changes for {MonthCount} months",
                monthlyChanges.Count);

            return monthlyChanges;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating stock value changes from {StartDate} to {EndDate}",
                startDate, endDate);
            throw;
        }
    }

    public async Task<MonthlyStockChange> GetStockValueChangeForPeriodAsync(
        DateTime periodStart,
        DateTime periodEndInclusive,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Calculating partial stock value change for period {Start:d}..{End:d}",
            periodStart, periodEndInclusive);

        var startTasks = new[]
        {
            GetWarehouseStockValueAsync(MaterialWarehouseId, periodStart, cancellationToken),
            GetWarehouseStockValueAsync(SemiProductsWarehouseId, periodStart, cancellationToken),
            GetWarehouseStockValueAsync(ProductsWarehouseId, periodStart, cancellationToken)
        };

        var endTasks = new[]
        {
            GetWarehouseStockValueAsync(MaterialWarehouseId, periodEndInclusive, cancellationToken),
            GetWarehouseStockValueAsync(SemiProductsWarehouseId, periodEndInclusive, cancellationToken),
            GetWarehouseStockValueAsync(ProductsWarehouseId, periodEndInclusive, cancellationToken)
        };

        var startValues = await Task.WhenAll(startTasks);
        var endValues = await Task.WhenAll(endTasks);

        return new MonthlyStockChange
        {
            Year = periodStart.Year,
            Month = periodStart.Month,
            StockChanges = new StockChangeByType
            {
                Materials = endValues[0] - startValues[0],
                SemiProducts = endValues[1] - startValues[1],
                Products = endValues[2] - startValues[2]
            }
        };
    }

    private async Task<MonthlyStockChange> CalculateMonthlyStockChangeAsync(
        DateTime monthStart,
        CancellationToken cancellationToken)
    {
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);

        // Get stock values at start and end of month for each warehouse
        var startStockTasks = new[]
        {
            GetWarehouseStockValueAsync(MaterialWarehouseId, monthStart, cancellationToken),
            GetWarehouseStockValueAsync(SemiProductsWarehouseId, monthStart, cancellationToken),
            GetWarehouseStockValueAsync(ProductsWarehouseId, monthStart, cancellationToken)
        };

        var endStockTasks = new[]
        {
            GetWarehouseStockValueAsync(MaterialWarehouseId, monthEnd, cancellationToken),
            GetWarehouseStockValueAsync(SemiProductsWarehouseId, monthEnd, cancellationToken),
            GetWarehouseStockValueAsync(ProductsWarehouseId, monthEnd, cancellationToken)
        };

        var startValues = await Task.WhenAll(startStockTasks);
        var endValues = await Task.WhenAll(endStockTasks);

        // Calculate changes (end - start for each warehouse)
        var materialsChange = endValues[0] - startValues[0];
        var semiProductsChange = endValues[1] - startValues[1];
        var productsChange = endValues[2] - startValues[2];

        return new MonthlyStockChange
        {
            Year = monthStart.Year,
            Month = monthStart.Month,
            StockChanges = new StockChangeByType
            {
                Materials = materialsChange,
                SemiProducts = semiProductsChange,
                Products = productsChange
            }
        };
    }

    private async Task<decimal> GetWarehouseStockValueAsync(
        int warehouseId,
        DateTime date,
        CancellationToken cancellationToken)
    {
        try
        {
            var stockItems = await _stockClient.StockToDateAsync(date, warehouseId, cancellationToken);

            var totalValue = stockItems.Sum(item => item.Stock * item.Price);

            _logger.LogDebug("Warehouse {WarehouseId} on {Date}: {ItemCount} items, value: {TotalValue:C}",
                warehouseId, date.ToShortDateString(), stockItems.Count, totalValue);

            return totalValue;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting stock value for warehouse {WarehouseId} on {Date}",
                warehouseId, date);
            return 0; // Return 0 if we can't get the data for this warehouse/date
        }
    }
}
