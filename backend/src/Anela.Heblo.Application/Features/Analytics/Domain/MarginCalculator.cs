using Anela.Heblo.Application.Features.Analytics.Contracts;

namespace Anela.Heblo.Application.Features.Analytics.Domain;

/// <summary>
/// 🔒 PERFORMANCE FIX: Extracted margin calculation logic from handler
/// Implements single responsibility principle and improves testability
/// </summary>
public class MarginCalculator
{
    /// <summary>
    /// Calculates margin data using streaming approach to minimize memory usage
    /// </summary>
    public async Task<MarginCalculationResult> CalculateAsync(
        IAsyncEnumerable<AnalyticsProduct> products,
        DateRange dateRange,
        ProductGroupingMode groupingMode,
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
            var marginContribution = (decimal)totalSold * product.MarginAmount;

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
}