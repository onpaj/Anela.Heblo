using System.Runtime.CompilerServices;
using Anela.Heblo.Application.Features.Analytics.Contracts;
using Anela.Heblo.Domain.Features.Analytics;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Sales;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

internal sealed class CatalogAnalyticsSourceAdapter : IAnalyticsProductSource
{
    private const int BatchSize = 100;
    private readonly ICatalogRepository _catalogRepository;

    public CatalogAnalyticsSourceAdapter(ICatalogRepository catalogRepository)
    {
        _catalogRepository = catalogRepository;
    }

    public async IAsyncEnumerable<AnalyticsProduct> StreamProductsWithSalesAsync(
        DateTime fromDate,
        DateTime toDate,
        AnalyticsProductType[] productTypes,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var catalogProductTypes = MapProductTypes(productTypes);
        var allProducts = await _catalogRepository.GetProductsWithSalesInPeriod(
            fromDate, toDate, catalogProductTypes, cancellationToken);

        for (int i = 0; i < allProducts.Count; i += BatchSize)
        {
            var batch = allProducts.Skip(i).Take(BatchSize);
            foreach (var product in batch)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var filteredSales = product.SalesHistory
                    .Where(s => s.Date >= fromDate && s.Date <= toDate)
                    .Select(s => new SalesDataPoint
                    {
                        Date = s.Date,
                        AmountB2B = s.AmountB2B,
                        AmountB2C = s.AmountB2C
                    })
                    .ToList();
                yield return MapToAnalyticsProduct(product, fromDate, toDate, filteredSales);
            }
            GC.Collect();
        }
    }

    public async Task<AnalyticsProduct?> GetProductAnalysisDataAsync(
        string productId,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default)
    {
        var product = await _catalogRepository.GetByIdAsync(productId, cancellationToken);
        if (product == null)
            return null;

        var filteredSales = product.SalesHistory
            .Where(s => s.Date >= fromDate && s.Date <= toDate)
            .Select(s => new SalesDataPoint
            {
                Date = s.Date,
                AmountB2B = s.AmountB2B,
                AmountB2C = s.AmountB2C
            })
            .ToList();

        return MapToAnalyticsProduct(product, fromDate, toDate, filteredSales);
    }

    private static AnalyticsProduct MapToAnalyticsProduct(
        CatalogAggregate product,
        DateTime fromDate,
        DateTime toDate,
        List<SalesDataPoint> salesHistory)
    {
        var marginData = product.Margins;
        var relevantMargins = marginData.MonthlyData
            .Where(m => m.Key >= fromDate && m.Key <= toDate)
            .ToList();

        var latestMarginEntry = relevantMargins.LastOrDefault();
        if (latestMarginEntry.Equals(default(KeyValuePair<DateTime, MarginData>)))
            latestMarginEntry = marginData.MonthlyData.LastOrDefault();

        bool hasMargin = !latestMarginEntry.Equals(default(KeyValuePair<DateTime, MarginData>));

        var marginAmount = hasMargin ? latestMarginEntry.Value.M0.Amount : marginData.Averages.M0.Amount;
        var materialCost = hasMargin ? latestMarginEntry.Value.M0.CostLevel : 0m;
        var handlingCost = hasMargin ? latestMarginEntry.Value.M1_A.CostLevel : 0m;

        var latestPurchase = product.PurchaseHistory?.OrderByDescending(p => p.Date).FirstOrDefault();
        var purchasePrice = latestPurchase?.PricePerPiece ?? 0m;

        return new AnalyticsProduct
        {
            ProductCode = product.ProductCode,
            ProductName = product.ProductName,
            Type = MapProductType(product.Type),
            ProductFamily = product.ProductFamily,
            ProductCategory = product.ProductCategory,
            MarginAmount = marginAmount,
            M0Amount = hasMargin ? latestMarginEntry.Value.M0.Amount : 0m,
            M1Amount = hasMargin ? latestMarginEntry.Value.M1_A.Amount : 0m,
            M2Amount = hasMargin ? latestMarginEntry.Value.M2.Amount : 0m,
            M0Percentage = hasMargin ? latestMarginEntry.Value.M0.Percentage : 0m,
            M1Percentage = hasMargin ? latestMarginEntry.Value.M1_A.Percentage : 0m,
            M2Percentage = hasMargin ? latestMarginEntry.Value.M2.Percentage : 0m,
            SellingPrice = product.EshopPrice?.PriceWithoutVat ?? 0m,
            EshopPriceWithoutVat = product.EshopPrice?.PriceWithoutVat,
            PurchasePrice = purchasePrice,
            MaterialCost = materialCost,
            HandlingCost = handlingCost,
            SalesHistory = salesHistory,
        };
    }

    private static ProductType[] MapProductTypes(AnalyticsProductType[] types) =>
        types.Select(MapProductTypeToCatalog).ToArray();

    private static ProductType MapProductTypeToCatalog(AnalyticsProductType type) => type switch
    {
        AnalyticsProductType.Product => ProductType.Product,
        AnalyticsProductType.Goods => ProductType.Goods,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type,
            "AnalyticsProductType has no Catalog.ProductType counterpart. Mirror the value in AnalyticsProductType and extend this switch."),
    };

    // Will be called in Task 6 when AnalyticsProduct.Type changes to AnalyticsProductType
    private static AnalyticsProductType MapProductType(ProductType type) => type switch
    {
        ProductType.Product => AnalyticsProductType.Product,
        ProductType.Goods => AnalyticsProductType.Goods,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type,
            "Adapter only projects Product and Goods."),
    };
}
