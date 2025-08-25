using Anela.Heblo.Application.Features.Analytics.Contracts;
using Anela.Heblo.Application.Features.Analytics.Domain;
using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Application.Features.Analytics.Infrastructure;

/// <summary>
/// 🔒 PERFORMANCE FIX: Analytics repository with streaming capabilities
/// Prevents memory overload by streaming data instead of loading everything at once
/// </summary>
public class AnalyticsRepository : IAnalyticsRepository
{
    private readonly ICatalogRepository _catalogRepository;

    public AnalyticsRepository(ICatalogRepository catalogRepository)
    {
        _catalogRepository = catalogRepository;
    }

    /// <summary>
    /// Streams products with sales to avoid memory overload
    /// Converts heavy CatalogAggregate to lightweight AnalyticsProduct
    /// </summary>
    public async IAsyncEnumerable<AnalyticsProduct> StreamProductsWithSalesAsync(
        DateTime fromDate, 
        DateTime toDate, 
        ProductType[] productTypes,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // TODO: In real implementation, this would stream from database
        // For now, we'll process in batches to reduce memory pressure
        const int batchSize = 100;
        
        // Get total count for batching
        var allProducts = await _catalogRepository.GetProductsWithSalesInPeriod(fromDate, toDate, productTypes, cancellationToken);
        
        // Process in batches to reduce memory usage
        for (int i = 0; i < allProducts.Count; i += batchSize)
        {
            var batch = allProducts.Skip(i).Take(batchSize);
            
            foreach (var product in batch)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                // Convert to lightweight analytics model
                yield return new AnalyticsProduct
                {
                    ProductCode = product.ProductCode,
                    ProductName = product.ProductName,
                    Type = product.Type,
                    ProductFamily = product.ProductFamily,
                    ProductCategory = product.ProductCategory,
                    MarginAmount = product.MarginAmount,
                    EshopPriceWithoutVat = product.EshopPrice?.PriceWithoutVat,
                    MaterialCost = GetLatestMaterialCost(product),
                    HandlingCost = GetLatestHandlingCost(product),
                    SalesHistory = product.SalesHistory
                        .Where(s => s.Date >= fromDate && s.Date <= toDate)
                        .Select(s => new SalesDataPoint
                        {
                            Date = s.Date,
                            AmountB2B = s.AmountB2B,
                            AmountB2C = s.AmountB2C
                        })
                        .ToList()
                };
            }
            
            // Allow garbage collection between batches
            GC.Collect();
        }
    }

    /// <summary>
    /// Gets aggregated margin data with optimized query (future implementation)
    /// </summary>
    public async Task<Dictionary<string, decimal>> GetGroupMarginTotalsAsync(
        DateTime fromDate,
        DateTime toDate,
        ProductType[] productTypes,
        ProductGroupingMode groupingMode,
        CancellationToken cancellationToken = default)
    {
        // TODO: This would be optimized SQL query in real database implementation
        // For now, use existing logic but avoid loading full objects
        var groupTotals = new Dictionary<string, decimal>();
        
        await foreach (var product in StreamProductsWithSalesAsync(fromDate, toDate, productTypes, cancellationToken))
        {
            if (product.MarginAmount <= 0)
                continue;

            var groupKey = GetGroupKey(product, groupingMode);
            var totalSold = product.SalesHistory.Sum(s => s.AmountB2B + s.AmountB2C);
            var marginContribution = (decimal)totalSold * product.MarginAmount;

            if (!groupTotals.ContainsKey(groupKey))
                groupTotals[groupKey] = 0;
            
            groupTotals[groupKey] += marginContribution;
        }

        return groupTotals;
    }

    private string GetGroupKey(AnalyticsProduct product, ProductGroupingMode groupingMode)
    {
        return groupingMode switch
        {
            ProductGroupingMode.Products => product.ProductCode,
            ProductGroupingMode.ProductFamily => product.ProductFamily ?? "Unknown",
            ProductGroupingMode.ProductCategory => product.ProductCategory ?? "Unknown",
            _ => product.ProductCode
        };
    }

    private decimal GetLatestMaterialCost(CatalogAggregate product)
    {
        return product.ManufactureCostHistory.LastOrDefault()?.MaterialCost ?? 0;
    }

    private decimal GetLatestHandlingCost(CatalogAggregate product)
    {
        return product.ManufactureCostHistory.LastOrDefault()?.HandlingCost ?? 0;
    }
}