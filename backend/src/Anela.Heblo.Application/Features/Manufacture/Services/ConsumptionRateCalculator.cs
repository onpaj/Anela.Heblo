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

        // Ensure correct date range order for filtering
        var actualFromDate = fromDate <= toDate ? fromDate : toDate;
        var actualToDate = fromDate <= toDate ? toDate : fromDate;

        var totalSales = salesHistory
            .Where(s => s.Date >= actualFromDate && s.Date <= actualToDate)
            .Sum(s => s.AmountB2B + s.AmountB2C);

        var dailyRate = totalSales / (double)daysDiff;

        _logger.LogDebug("Calculated daily sales rate: {Rate} for period {FromDate:yyyy-MM-dd} to {ToDate:yyyy-MM-dd} ({Days} days, total sales: {Total})",
            dailyRate, fromDate, toDate, daysDiff, totalSales);

        return dailyRate;
    }

    public double CalculateDailySalesRate(IEnumerable<CatalogSaleRecord> salesHistory, IReadOnlyList<(DateTime fromDate, DateTime toDate)> ranges)
    {
        if (ranges == null || ranges.Count == 0)
        {
            return 0;
        }

        var materializedHistory = salesHistory as IList<CatalogSaleRecord> ?? salesHistory.ToList();

        double totalSales = 0;
        int totalDays = 0;

        foreach (var (from, to) in ranges)
        {
            var lo = from <= to ? from : to;
            var hi = from <= to ? to : from;
            var days = (hi - lo).Days;
            if (days <= 0) days = 1;
            totalDays += days;
            totalSales += materializedHistory
                .Where(s => s.Date >= lo && s.Date <= hi)
                .Sum(s => s.AmountB2B + s.AmountB2C);
        }

        if (totalDays == 0)
        {
            return 0;
        }

        var dailyRate = totalSales / (double)totalDays;
        _logger.LogDebug(
            "Calculated daily sales rate across {RangeCount} ranges: {Rate} ({TotalDays} days, total sales: {Total})",
            ranges.Count, dailyRate, totalDays, totalSales);

        return dailyRate;
    }

    public double CalculateDailyConsumptionRate(IEnumerable<ConsumedMaterialRecord> consumedHistory, DateTime fromDate, DateTime toDate)
    {
        var daysDiff = (toDate - fromDate).Days;
        if (daysDiff <= 0) daysDiff = 1;

        // Ensure correct date range order for filtering
        var actualFromDate = fromDate <= toDate ? fromDate : toDate;
        var actualToDate = fromDate <= toDate ? toDate : fromDate;

        var totalConsumption = consumedHistory
            .Where(c => c.Date >= actualFromDate && c.Date <= actualToDate)
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