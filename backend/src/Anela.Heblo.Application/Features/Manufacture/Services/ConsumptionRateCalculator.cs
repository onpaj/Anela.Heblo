using Anela.Heblo.Domain.Features.Catalog.ConsumedMaterials;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Manufacture.Services;

public class ConsumptionRateCalculator : IConsumptionRateCalculator
{
    private const double InfiniteStockDays = 999999.0;
    private const double MinimumConsumptionRate = 0.000001;
    private readonly ILogger<ConsumptionRateCalculator> _logger;

    public ConsumptionRateCalculator(ILogger<ConsumptionRateCalculator> logger)
    {
        _logger = logger;
    }

    public double CalculateDailySalesRate(IEnumerable<CatalogSaleRecord> salesHistory, DateTime fromDate, DateTime toDate)
    {
        var daysDiff = (toDate - fromDate).Days;
        if (daysDiff <= 0) daysDiff = 1;

        var totalSales = salesHistory
            .Where(s => s.Date >= fromDate && s.Date <= toDate)
            .Sum(s => s.AmountB2B + s.AmountB2C);

        var dailyRate = totalSales / (double)daysDiff;

        _logger.LogDebug("Calculated daily sales rate: {Rate} for period {FromDate:yyyy-MM-dd} to {ToDate:yyyy-MM-dd} ({Days} days, total sales: {Total})",
            dailyRate, fromDate, toDate, daysDiff, totalSales);

        return dailyRate;
    }

    public double CalculateDailyConsumptionRate(IEnumerable<ConsumedMaterialRecord> consumedHistory, DateTime fromDate, DateTime toDate)
    {
        var daysDiff = (toDate - fromDate).Days;
        if (daysDiff <= 0) daysDiff = 1;

        var totalConsumption = consumedHistory
            .Where(c => c.Date >= fromDate && c.Date <= toDate)
            .Sum(c => c.Amount);

        var dailyRate = totalConsumption / (double)daysDiff;

        _logger.LogDebug("Calculated daily consumption rate: {Rate} for period {FromDate:yyyy-MM-dd} to {ToDate:yyyy-MM-dd} ({Days} days, total consumption: {Total})",
            dailyRate, fromDate, toDate, daysDiff, totalConsumption);

        return dailyRate;
    }

    public double CalculateStockDaysAvailable(decimal availableStock, double dailyConsumptionRate)
    {
        if (dailyConsumptionRate < MinimumConsumptionRate)
        {
            _logger.LogDebug("Daily consumption rate {Rate} is below minimum threshold, returning infinite stock days", dailyConsumptionRate);
            return InfiniteStockDays;
        }

        var stockDays = (double)availableStock / dailyConsumptionRate;

        _logger.LogDebug("Calculated stock days available: {StockDays} (stock: {Stock}, daily rate: {Rate})",
            stockDays, availableStock, dailyConsumptionRate);

        return stockDays;
    }
}