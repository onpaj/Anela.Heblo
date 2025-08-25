using Anela.Heblo.Application.Features.Manufacture.Model;
using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Application.Features.Manufacture.Services;

public class ManufactureAnalysisMapper : IManufactureAnalysisMapper
{
    public ManufacturingStockItemDto MapToDto(
        CatalogAggregate item,
        DateTime fromDate,
        DateTime toDate,
        ManufacturingStockSeverity severity)
    {
        var (salesInPeriod, dailySalesRate, stockDaysAvailable, overstockPercentage) = CalculateStockMetrics(item, fromDate, toDate);

        return new ManufacturingStockItemDto
        {
            Code = item.ProductCode,
            Name = item.ProductName,
            CurrentStock = (double)item.Stock.Available,
            SalesInPeriod = salesInPeriod,
            DailySalesRate = dailySalesRate,
            OptimalDaysSetup = item.Properties.OptimalStockDaysSetup,
            StockDaysAvailable = double.IsInfinity(stockDaysAvailable) ? 999999 : stockDaysAvailable, // Cap infinity for display
            MinimumStock = (double)item.Properties.StockMinSetup,
            OverstockPercentage = double.IsInfinity(overstockPercentage) ? 0 : overstockPercentage,
            BatchSize = item.Properties.BatchSize.ToString(),
            ProductFamily = item.ProductFamily ?? string.Empty,
            Severity = severity,
            IsConfigured = item.Properties.OptimalStockDaysSetup > 0
        };
    }

    public (double salesInPeriod, double dailySalesRate, double stockDaysAvailable, double overstockPercentage) CalculateStockMetrics(
        CatalogAggregate item,
        DateTime fromDate,
        DateTime toDate)
    {
        var daysDiff = (toDate - fromDate).Days;
        if (daysDiff <= 0) daysDiff = 1;

        // For finished products, use sales data
        var salesInPeriod = item.GetTotalSold(fromDate, toDate);
        var dailySalesRate = salesInPeriod / (double)daysDiff;

        // Calculate stock days available
        var stockDaysAvailable = dailySalesRate > 0
            ? (double)item.Stock.Available / dailySalesRate
            : double.PositiveInfinity;

        // Calculate overstock percentage
        var overstockPercentage = item.Properties.OptimalStockDaysSetup > 0
            ? (stockDaysAvailable / item.Properties.OptimalStockDaysSetup) * 100
            : 0;

        return (salesInPeriod, dailySalesRate, stockDaysAvailable, overstockPercentage);
    }
}