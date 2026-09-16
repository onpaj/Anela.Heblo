using Anela.Heblo.Application.Features.Purchase.Contracts;
using Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseStockAnalysis;

namespace Anela.Heblo.Application.Features.Purchase.Services;

/// <summary>
/// Service responsible for calculating stock analysis metrics such as efficiency and recommended order quantity.
/// </summary>
public class StockAnalysisCalculator : IStockAnalysisCalculator
{
    private readonly IStockSeverityCalculator _stockSeverityCalculator;

    public StockAnalysisCalculator(IStockSeverityCalculator stockSeverityCalculator)
    {
        _stockSeverityCalculator = stockSeverityCalculator;
    }

    /// <summary>
    /// Calculates the stock efficiency percentage based on available stock relative to optimal or minimum stock.
    /// </summary>
    /// <param name="availableStock">Current available stock amount</param>
    /// <param name="minStock">Configured minimum stock level</param>
    /// <param name="optimalStock">Calculated optimal stock level</param>
    /// <returns>Stock efficiency percentage</returns>
    public double CalculateStockEfficiency(double availableStock, double minStock, double optimalStock)
    {
        if (optimalStock <= 0)
        {
            return minStock > 0 ? (availableStock / minStock) * 100 : 0;
        }

        return (availableStock / optimalStock) * 100;
    }

    /// <summary>
    /// Calculates the recommended order quantity needed to reach the target stock level.
    /// </summary>
    /// <param name="availableStock">Current available stock amount</param>
    /// <param name="optimalStock">Calculated optimal stock level</param>
    /// <param name="minStock">Configured minimum stock level</param>
    /// <param name="moq">Minimum order quantity as configured for the material</param>
    /// <returns>Recommended order quantity, or null if no order is needed</returns>
    public double? CalculateRecommendedOrderQuantity(double availableStock, double optimalStock, double minStock, string moq)
    {
        if (optimalStock <= 0 && minStock <= 0)
        {
            return null;
        }

        var targetStock = optimalStock > 0 ? optimalStock : minStock * 2;
        var needed = targetStock - availableStock;

        if (needed <= 0)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(moq) && double.TryParse(moq, out var minOrderQty))
        {
            return Math.Max(needed, minOrderQty);
        }

        return needed;
    }

    public StockAnalysisItemDto AnalyzeItem(MaterialStockSnapshot item, DateTime fromDate, DateTime toDate)
    {
        var daysDiff = (toDate - fromDate).Days;
        if (daysDiff <= 0) daysDiff = 1;

        var consumption = item.ConsumptionInPeriod;
        var dailyConsumption = consumption / (double)daysDiff;

        int? daysUntilStockout = null;
        if (dailyConsumption > 0)
        {
            daysUntilStockout = (int)((double)item.Stock.EffectiveStock / dailyConsumption);
        }

        var minStock = item.StockMinSetup;
        var optimalStockDays = item.OptimalStockDaysSetup;
        var optimalStock = optimalStockDays > 0 ? dailyConsumption * (double)optimalStockDays : 0;

        var stockEfficiency = CalculateStockEfficiency((double)item.Stock.EffectiveStock, (double)minStock, optimalStock);
        var severity = _stockSeverityCalculator.DetermineStockSeverity((double)item.Stock.EffectiveStock, (double)minStock, optimalStock, item.IsMinStockConfigured, item.IsOptimalStockConfigured);

        var lastPurchase = GetLastPurchaseInfo(item);

        var recommendedQuantity = CalculateRecommendedOrderQuantity(
            (double)item.Stock.Available,
            optimalStock,
            (double)minStock,
            item.MinimalOrderQuantity);

        return new StockAnalysisItemDto
        {
            ProductCode = item.ProductCode,
            ProductName = item.ProductName,
            ProductNameNormalized = item.ProductNameNormalized,
            ProductType = item.ProductType.ToString(),
            AvailableStock = (double)item.Stock.Available,
            OrderedStock = (double)item.Stock.Ordered,
            EffectiveStock = (double)item.Stock.EffectiveStock,
            MinStockLevel = (double)minStock,
            OptimalStockLevel = optimalStock,
            ConsumptionInPeriod = consumption,
            DailyConsumption = dailyConsumption,
            DaysUntilStockout = daysUntilStockout,
            StockEfficiencyPercentage = stockEfficiency,
            Severity = severity,
            MinimalOrderQuantity = item.MinimalOrderQuantity,
            LastPurchase = lastPurchase,
            Supplier = item.SupplierName,
            RecommendedOrderQuantity = recommendedQuantity,
            IsConfigured = item.IsMinStockConfigured || item.IsOptimalStockConfigured
        };
    }

    public List<StockAnalysisItemDto> FilterItems(List<StockAnalysisItemDto> items, GetPurchaseStockAnalysisRequest request)
    {
        return items.Where(item => ShouldIncludeItem(item, request)).ToList();
    }

    public List<StockAnalysisItemDto> SortItems(List<StockAnalysisItemDto> items, StockAnalysisSortBy sortBy, bool descending)
    {
        var sorted = sortBy switch
        {
            StockAnalysisSortBy.ProductCode => items.OrderBy(i => i.ProductCode),
            StockAnalysisSortBy.ProductName => items.OrderBy(i => i.ProductName),
            StockAnalysisSortBy.AvailableStock => items.OrderBy(i => i.AvailableStock),
            StockAnalysisSortBy.Consumption => items.OrderBy(i => i.ConsumptionInPeriod),
            StockAnalysisSortBy.StockEfficiency => items.OrderBy(i => i.StockEfficiencyPercentage),
            StockAnalysisSortBy.LastPurchaseDate => items.OrderBy(i => i.LastPurchase?.Date ?? DateTime.MinValue),
            _ => items.OrderBy(i => i.StockEfficiencyPercentage)
        };

        return descending ? sorted.Reverse().ToList() : sorted.ToList();
    }

    public StockAnalysisSummaryDto CalculateSummary(List<StockAnalysisItemDto> items, DateTime fromDate, DateTime toDate)
    {
        return new StockAnalysisSummaryDto
        {
            TotalProducts = items.Count,
            CriticalCount = items.Count(i => i.Severity == StockSeverity.Critical),
            LowStockCount = items.Count(i => i.Severity == StockSeverity.Low),
            OptimalCount = items.Count(i => i.Severity == StockSeverity.Optimal),
            OverstockedCount = items.Count(i => i.Severity == StockSeverity.Overstocked),
            NotConfiguredCount = items.Count(i => i.Severity == StockSeverity.NotConfigured),
            TotalInventoryValue = items.Sum(i => (decimal)i.EffectiveStock * (i.LastPurchase?.UnitPrice ?? 0)),
            AnalysisPeriodStart = fromDate,
            AnalysisPeriodEnd = toDate
        };
    }

    private bool ShouldIncludeItem(StockAnalysisItemDto item, GetPurchaseStockAnalysisRequest request)
    {
        if (request.OnlyConfigured && !item.IsConfigured)
        {
            return false;
        }

        return request.StockStatus switch
        {
            StockStatusFilter.Critical => item.Severity == StockSeverity.Critical,
            StockStatusFilter.Low => item.Severity == StockSeverity.Low,
            StockStatusFilter.Optimal => item.Severity == StockSeverity.Optimal,
            StockStatusFilter.Overstocked => item.Severity == StockSeverity.Overstocked,
            StockStatusFilter.NotConfigured => item.Severity == StockSeverity.NotConfigured,
            _ => true
        };
    }

    private LastPurchaseInfoDto? GetLastPurchaseInfo(MaterialStockSnapshot item)
    {
        var lastPurchase = item.LastPurchase;

        if (lastPurchase == null)
        {
            return null;
        }

        return new LastPurchaseInfoDto
        {
            Date = lastPurchase.Date,
            SupplierName = lastPurchase.SupplierName,
            Amount = (double)lastPurchase.Amount,
            UnitPrice = lastPurchase.UnitPrice,
            TotalPrice = lastPurchase.TotalPrice
        };
    }
}
