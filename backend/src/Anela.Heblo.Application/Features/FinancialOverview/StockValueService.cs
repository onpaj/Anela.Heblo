using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.FinancialOverview;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.FinancialOverview;

/// <summary>
/// Real implementation that calculates stock value changes using StockToDate integration
/// </summary>
public class StockValueService : IStockValueService
{
    private readonly IErpStockClient _stockClient;
    private readonly IProductPriceErpClient _priceClient;
    private readonly ILogger<StockValueService> _logger;

    // Warehouse IDs from FlexiStockClient
    private const int MaterialWarehouseId = 5;    // MATERIAL
    private const int SemiProductsWarehouseId = 20; // POLOTOVARY  
    private const int ProductsWarehouseId = 4;    // ZBOZI

    public StockValueService(
        IErpStockClient stockClient,
        IProductPriceErpClient priceClient,
        ILogger<StockValueService> logger)
    {
        _stockClient = stockClient;
        _priceClient = priceClient;
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
            // Get all product prices for value calculations
            var prices = await _priceClient.GetAllAsync(forceReload: false, cancellationToken);
            var priceDict = prices.ToDictionary(p => p.ProductCode, p => p.PurchasePrice);

            var monthlyChanges = new List<MonthlyStockChange>();

            // Process each month in the date range
            var currentDate = new DateTime(startDate.Year, startDate.Month, 1);
            var endMonth = new DateTime(endDate.Year, endDate.Month, 1);

            while (currentDate <= endMonth)
            {
                _logger.LogDebug("Processing stock changes for {Year}/{Month}", currentDate.Year, currentDate.Month);

                var monthlyChange = await CalculateMonthlyStockChangeAsync(
                    currentDate, priceDict, cancellationToken);

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

    private async Task<MonthlyStockChange> CalculateMonthlyStockChangeAsync(
        DateTime monthStart,
        Dictionary<string, decimal> priceDict,
        CancellationToken cancellationToken)
    {
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);

        // Get stock values at start and end of month for each warehouse
        var startStockTasks = new[]
        {
            GetWarehouseStockValueAsync(MaterialWarehouseId, monthStart, priceDict, cancellationToken),
            GetWarehouseStockValueAsync(SemiProductsWarehouseId, monthStart, priceDict, cancellationToken),
            GetWarehouseStockValueAsync(ProductsWarehouseId, monthStart, priceDict, cancellationToken)
        };

        var endStockTasks = new[]
        {
            GetWarehouseStockValueAsync(MaterialWarehouseId, monthEnd, priceDict, cancellationToken),
            GetWarehouseStockValueAsync(SemiProductsWarehouseId, monthEnd, priceDict, cancellationToken),
            GetWarehouseStockValueAsync(ProductsWarehouseId, monthEnd, priceDict, cancellationToken)
        };

        await Task.WhenAll(startStockTasks.Concat(endStockTasks));

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
        Dictionary<string, decimal> priceDict,
        CancellationToken cancellationToken)
    {
        try
        {
            var stockItems = await _stockClient.StockToDateAsync(date, warehouseId, cancellationToken);

            decimal totalValue = 0;
            var processedItems = 0;
            var missingPrices = 0;

            foreach (var item in stockItems)
            {
                if (priceDict.TryGetValue(item.ProductCode, out var purchasePrice))
                {
                    totalValue += item.Stock * purchasePrice;
                    processedItems++;
                }
                else
                {
                    missingPrices++;
                    _logger.LogDebug("No purchase price found for product {ProductCode} in warehouse {WarehouseId}",
                        item.ProductCode, warehouseId);
                }
            }

            _logger.LogDebug("Warehouse {WarehouseId} on {Date}: {ProcessedItems} items, {MissingPrices} missing prices, value: {TotalValue:C}",
                warehouseId, date.ToShortDateString(), processedItems, missingPrices, totalValue);

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