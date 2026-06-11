using Anela.Heblo.Application.Features.Analytics.Contracts;
using Anela.Heblo.Domain.Features.Analytics;

namespace Anela.Heblo.Application.Features.Analytics.Services;

public interface IMarginCalculator
{
    Task<MarginCalculationResult> CalculateAsync(
        IAsyncEnumerable<AnalyticsProduct> products,
        DateRange dateRange,
        ProductGroupingMode groupingMode,
        MarginLevel marginLevel = MarginLevel.M2,
        CancellationToken cancellationToken = default);

    string GetGroupKey(AnalyticsProduct product, ProductGroupingMode groupingMode);

    string GetGroupDisplayName(
        string groupKey,
        ProductGroupingMode groupingMode,
        List<AnalyticsProduct> products);

    decimal GetMarginAmountForLevel(AnalyticsProduct product, MarginLevel marginLevel);

    /// <remarks>
    /// Caller must pre-filter salesInPeriod to the desired period; the calculator sums verbatim.
    /// salesInPeriod is enumerated exactly once. Unlike CalculateAsync, does not skip products
    /// with MarginAmount ≤ 0; per-product callers report them with zero margin.
    /// </remarks>
    AnalysisMarginData CalculateForProduct(
        AnalyticsProduct product,
        IEnumerable<SalesDataPoint> salesInPeriod);
}

/// <summary>
/// 🔒 PERFORMANCE FIX: Extracted margin calculation logic from handler
/// Implements single responsibility principle and improves testability
/// </summary>
public class MarginCalculator : IMarginCalculator
{
    /// <summary>
    /// Calculates margin data using streaming approach to minimize memory usage
    /// </summary>
    public async Task<MarginCalculationResult> CalculateAsync(
        IAsyncEnumerable<AnalyticsProduct> products,
        DateRange dateRange,
        ProductGroupingMode groupingMode,
        MarginLevel marginLevel = MarginLevel.M2,
        CancellationToken cancellationToken = default)
    {
        var groupTotals = new Dictionary<string, decimal>();
        var groupProducts = new Dictionary<string, List<AnalyticsProduct>>();
        var totalMargin = 0m;

        await foreach (var product in products.WithCancellation(cancellationToken))
        {
            if (product.MarginAmount <= 0)
                continue;

            var groupKey = GetGroupKey(product, groupingMode);

            // Calculate total units sold in the period
            var totalSold = product.SalesHistory.Sum(s => s.AmountB2B + s.AmountB2C);
            var marginContribution = (decimal)totalSold * GetMarginAmountForLevel(product, marginLevel);

            // Update group totals
            if (!groupTotals.ContainsKey(groupKey))
            {
                groupTotals[groupKey] = 0;
                groupProducts[groupKey] = new List<AnalyticsProduct>();
            }

            groupTotals[groupKey] += marginContribution;
            groupProducts[groupKey].Add(product);
            totalMargin += marginContribution;
        }

        return new MarginCalculationResult
        {
            GroupTotals = groupTotals,
            GroupProducts = groupProducts,
            TotalMargin = totalMargin
        };
    }

    /// <summary>
    /// Gets the group key based on grouping mode
    /// </summary>
    public string GetGroupKey(AnalyticsProduct product, ProductGroupingMode groupingMode)
    {
        return groupingMode switch
        {
            ProductGroupingMode.Products => product.ProductCode,
            ProductGroupingMode.ProductFamily => product.ProductFamily ?? "Unknown",
            ProductGroupingMode.ProductCategory => product.ProductCategory ?? "Unknown",
            _ => product.ProductCode
        };
    }

    /// <summary>
    /// Gets display name for a group
    /// </summary>
    public string GetGroupDisplayName(string groupKey, ProductGroupingMode groupingMode, List<AnalyticsProduct> products)
    {
        return groupingMode switch
        {
            ProductGroupingMode.Products => products.FirstOrDefault(p => p.ProductCode == groupKey)?.ProductName ?? groupKey,
            ProductGroupingMode.ProductFamily => $"Rodina {groupKey}",
            ProductGroupingMode.ProductCategory => $"Kategorie {groupKey}",
            _ => groupKey
        };
    }

    /// <summary>
    /// Gets the margin amount for a specific margin level
    /// </summary>
    public decimal GetMarginAmountForLevel(AnalyticsProduct product, MarginLevel marginLevel)
    {
        return marginLevel switch
        {
            MarginLevel.M0 => product.M0Amount,
            MarginLevel.M1 => product.M1Amount,
            MarginLevel.M2 => product.M2Amount,
            _ => throw new ArgumentOutOfRangeException(nameof(marginLevel), marginLevel, null),
        };
    }

    /// <summary>
    /// Calculates margin data for a specific product based on sales in period
    /// </summary>
    public AnalysisMarginData CalculateForProduct(
        AnalyticsProduct product,
        IEnumerable<SalesDataPoint> salesInPeriod)
    {
        var units = (int)salesInPeriod.Sum(s => s.AmountB2B + s.AmountB2C);
        var revenue = (decimal)units * product.SellingPrice;
        var cost = (decimal)units * (product.SellingPrice - product.MarginAmount);
        var margin = revenue - cost;
        var marginPercentage = revenue > 0 ? (margin / revenue) * 100m : 0m;

        return new AnalysisMarginData
        {
            Revenue = revenue,
            Cost = cost,
            Margin = margin,
            MarginPercentage = marginPercentage,
            UnitsSold = units,
        };
    }
}
