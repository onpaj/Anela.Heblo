using Anela.Heblo.Application.Features.Analytics.Contracts;
using Anela.Heblo.Domain.Features.Analytics;
using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Application.Features.Analytics.Services;

public class ProductMarginAnalysisService : IProductMarginAnalysisService
{
    public (DateTime fromDate, DateTime toDate) ParseTimeWindow(string timeWindow)
    {
        var today = DateTime.Today;

        return timeWindow switch
        {
            "current-year" => (new DateTime(today.Year, 1, 1), today),
            "current-and-previous-year" => (new DateTime(today.Year - 1, 1, 1), today),
            "last-6-months" => (today.AddMonths(-6), today),
            "last-12-months" => (today.AddMonths(-12), today),
            "last-24-months" => (today.AddMonths(-24), today),
            _ => (new DateTime(today.Year, 1, 1), today)
        };
    }

    public Dictionary<string, decimal> CalculateGroupTotalMargin(List<CatalogAggregate> products, DateTime fromDate, DateTime toDate, ProductGroupingMode groupingMode)
    {
        var groupMarginMap = new Dictionary<string, decimal>();

        foreach (var product in products)
        {
            if (product.MarginAmount <= 0)
                continue;

            // Get group key for this product
            var groupKey = GetGroupKey(product, groupingMode);

            // Get total units sold in the period
            var totalSold = product.SalesHistory
                .Where(s => s.Date >= fromDate && s.Date <= toDate)
                .Sum(s => s.AmountB2B + s.AmountB2C);

            // Calculate total margin contribution (units sold * margin per piece)
            var totalMargin = totalSold * (double)product.MarginAmount;

            // Add to group total
            if (groupMarginMap.ContainsKey(groupKey))
                groupMarginMap[groupKey] += (decimal)totalMargin;
            else
                groupMarginMap[groupKey] = (decimal)totalMargin;
        }

        return groupMarginMap;
    }

    public string GetGroupKey(CatalogAggregate product, ProductGroupingMode groupingMode)
    {
        return groupingMode switch
        {
            ProductGroupingMode.Products => product.ProductCode,
            ProductGroupingMode.ProductFamily => product.ProductFamily ?? "Unknown",
            ProductGroupingMode.ProductCategory => product.ProductCategory ?? "Unknown",
            _ => product.ProductCode
        };
    }

    public string GetGroupDisplayName(string groupKey, ProductGroupingMode groupingMode, List<CatalogAggregate> products)
    {
        return groupingMode switch
        {
            ProductGroupingMode.Products => products.FirstOrDefault(p => p.ProductCode == groupKey)?.ProductName ?? groupKey,
            ProductGroupingMode.ProductFamily => $"Rodina {groupKey}",
            ProductGroupingMode.ProductCategory => $"Kategorie {groupKey}",
            _ => groupKey
        };
    }

    private string GetProductTypeDisplayName(ProductType type)
    {
        return type switch
        {
            ProductType.Product => "Produkty",
            ProductType.Goods => "Zboží",
            ProductType.Material => "Materiály",
            ProductType.SemiProduct => "Polotovary",
            _ => "Ostatní"
        };
    }

    public decimal CalculateMaterialCosts(CatalogAggregate product)
    {
        // Get latest manufacturing costs or return zero if not available
        var latestCost = product.ManufactureCostHistory.LastOrDefault();
        return latestCost?.MaterialCost ?? 0;
    }

    public decimal CalculateLaborCosts(CatalogAggregate product)
    {
        // Get latest manufacturing costs or return zero if not available  
        var latestCost = product.ManufactureCostHistory.LastOrDefault();
        return latestCost?.HandlingCost ?? 0;
    }
}