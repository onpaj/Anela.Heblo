using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Attributes;
using Anela.Heblo.Domain.Features.Catalog.ConsumedMaterials;
using Anela.Heblo.Domain.Features.Catalog.Lots;
using Anela.Heblo.Domain.Features.Catalog.EshopUrl;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.Catalog.PurchaseHistory;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using Anela.Heblo.Domain.Features.Catalog.ManufactureHistory;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

/// <summary>
/// Encapsulates the logic for merging catalog data from multiple sources into a unified aggregate.
/// </summary>
public sealed class CatalogMergeService
{
    private readonly CatalogCacheStore _cacheStore;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CatalogMergeService> _logger;

    public CatalogMergeService(
        CatalogCacheStore cacheStore,
        TimeProvider timeProvider,
        ILogger<CatalogMergeService> logger)
    {
        _cacheStore = cacheStore ?? throw new ArgumentNullException(nameof(cacheStore));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes the background merge asynchronously and atomically updates the cache.
    /// </summary>
    public async Task ExecuteBackgroundMergeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var newCatalogData = await Task.Run(() => Merge(), cancellationToken);
            await _cacheStore.ReplaceCacheAtomicallyAsync(newCatalogData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Background merge failed");
            throw;
        }
    }

    /// <summary>
    /// Executes a priority merge (synchronous) when cache is unavailable.
    /// Public to allow CatalogRepository to call when cache is empty.
    /// </summary>
    public async Task<List<CatalogAggregate>> ExecutePriorityMergeAsync()
    {
        _logger.LogInformation("Executing priority merge - no cache available");
        var newCatalogData = await Task.Run(() => Merge());
        await _cacheStore.ReplaceCacheAtomicallyAsync(newCatalogData);
        return newCatalogData;
    }

    /// <summary>
    /// Merges all catalog data sources into a unified list of CatalogAggregate objects.
    /// </summary>
    internal List<CatalogAggregate> Merge()
    {
        List<CatalogAggregate> products = new List<CatalogAggregate>();
        var catalogData = _cacheStore.GetCatalogData();

        if (!catalogData.Any())
        {
            // Bootstrap from ERP stock data if no existing catalog
            products = _cacheStore.GetErpStockData()
                .Select(s => new CatalogAggregate()
                {
                    ProductCode = s.ProductCode
                }).ToList();
        }
        else
        {
            products = catalogData;
        }

        // Build all lookup maps from cache data
        var attributesMap = _cacheStore.GetCatalogAttributesData().ToDictionary(k => k.ProductCode, v => v);
        var eshopProductsMap = _cacheStore.GetEshopStockData().ToDictionary(k => k.Code, v => v);
        var erpProductsMap = _cacheStore.GetErpStockData().ToDictionary(k => k.ProductCode, v => v);
        var consumedMap = _cacheStore.GetConsumedData()
            .GroupBy(p => p.ProductCode)
            .ToDictionary(k => k.Key, v => v.ToList());
        var purchaseMap = _cacheStore.GetPurchaseHistoryData()
            .GroupBy(p => p.ProductCode)
            .ToDictionary(k => k.Key, v => v.ToList());
        var manufactureMap = _cacheStore.GetManufactureHistoryData()
            .GroupBy(p => p.ProductCode)
            .ToDictionary(k => k.Key, v => v.ToList());
        var stockTakingMap = _cacheStore.GetStockTakingData()
            .GroupBy(p => p.Code)
            .ToDictionary(k => k.Key, v => v.ToList());
        var lotsMap = _cacheStore.GetLotsData()
            .GroupBy(p => p.ProductCode)
            .ToDictionary(k => k.Key, v => v.ToList());
        var eshopPriceMap = _cacheStore.GetEshopPriceData().ToDictionary(k => k.ProductCode, v => v);
        var erpPriceMap = _cacheStore.GetErpPriceData().ToDictionary(k => k.ProductCode, v => v);
        var eshopUrlMap = _cacheStore.GetEshopUrlData().ToDictionary(k => k.ProductCode, v => v.Url);
        var inTransportData = _cacheStore.GetInTransportData();
        var manufacturedData = _cacheStore.GetManufacturedData();
        var inReserveData = _cacheStore.GetInReserveData();
        var inQuarantineData = _cacheStore.GetInQuarantineData();
        var orderedData = _cacheStore.GetOrderedData();
        var plannedData = _cacheStore.GetPlannedData();
        var salesData = _cacheStore.GetSalesData();
        var manufactureDifficultyData = _cacheStore.GetManufactureDifficultySettingsData();

        // Merge data into each product
        foreach (var product in products)
        {
            MergeErpData(product, erpProductsMap);
            MergeSalesHistory(product, salesData);
            MergeAttributes(product, attributesMap);
            MergeStockData(product, inTransportData, manufacturedData, inReserveData, inQuarantineData, orderedData, plannedData);
            MergeEshopData(product, eshopProductsMap);
            MergeHistoryData(product, consumedMap, purchaseMap, manufactureMap, stockTakingMap);
            MergeLots(product, lotsMap);
            MergePrices(product, eshopPriceMap, erpPriceMap);
            MergeEshopUrl(product, eshopUrlMap);
            MergeManufactureDifficultySettings(product, manufactureDifficultyData);
        }

        // Set last merge timestamp
        _cacheStore.SetLastMergeDateTime();

        return products.ToList();
    }

    private static void MergeErpData(CatalogAggregate product, IDictionary<string, ErpStock> erpProductsMap)
    {
        if (erpProductsMap.TryGetValue(product.ProductCode, out var erpProduct))
        {
            product.ProductName = erpProduct.ProductName;
            product.ErpId = erpProduct.ProductId;
            product.Stock.Erp = erpProduct.Stock;
            product.Type = GetProductType(erpProduct);
            product.MinimalOrderQuantity = erpProduct.MOQ;
            product.HasLots = erpProduct.HasLots;
            product.HasExpiration = erpProduct.HasExpiration;
            product.Volume = erpProduct.Volume;
            product.NetWeight = erpProduct.Weight;
            product.Note = erpProduct.Note;
            product.SupplierCode = erpProduct.SupplierCode;
            product.SupplierName = erpProduct.SupplierName;
        }
    }

    private static void MergeSalesHistory(CatalogAggregate product, IList<CatalogSaleRecord> salesData)
    {
        product.SalesHistory = salesData.Where(w => w.ProductCode == product.ProductCode).ToList();
    }

    private static void MergeAttributes(CatalogAggregate product, IDictionary<string, CatalogAttributes> attributesMap)
    {
        if (attributesMap.TryGetValue(product.ProductCode, out var attributes))
        {
            product.Properties.OptimalStockDaysSetup = attributes.OptimalStockDays;
            product.Properties.StockMinSetup = attributes.StockMin;
            product.Properties.BatchSize = attributes.BatchSize;
            product.Properties.ExpirationMonths = attributes.ExpirationMonths;
            product.Properties.SeasonMonths = attributes.SeasonMonthsArray;
            product.MinimalManufactureQuantity = attributes.MinimalManufactureQuantity;
            product.Properties.AllowedResiduePercentage = attributes.AllowedResiduePercentage;
            product.Properties.Cooling = attributes.Cooling;
        }
    }

    private static void MergeStockData(
        CatalogAggregate product,
        IDictionary<string, int> inTransportData,
        IDictionary<string, decimal> manufacturedData,
        IDictionary<string, int> inReserveData,
        IDictionary<string, int> inQuarantineData,
        IDictionary<string, decimal> orderedData,
        IDictionary<string, decimal> plannedData)
    {
        product.Stock.Transport = inTransportData.ContainsKey(product.ProductCode) ? inTransportData[product.ProductCode] : 0;
        product.Stock.Manufactured = manufacturedData.ContainsKey(product.ProductCode) ? manufacturedData[product.ProductCode] : 0;
        product.Stock.Reserve = inReserveData.ContainsKey(product.ProductCode) ? inReserveData[product.ProductCode] : 0;
        product.Stock.Quarantine = inQuarantineData.ContainsKey(product.ProductCode) ? inQuarantineData[product.ProductCode] : 0;
        product.Stock.Ordered = orderedData.ContainsKey(product.ProductCode) ? orderedData[product.ProductCode] : 0;
        product.Stock.Planned = plannedData.ContainsKey(product.ProductCode) ? plannedData[product.ProductCode] : 0;
    }

    private static void MergeEshopData(CatalogAggregate product, IDictionary<string, EshopStock> eshopProductsMap)
    {
        if (eshopProductsMap.TryGetValue(product.ProductCode, out var eshopProduct))
        {
            product.Stock.Eshop = eshopProduct.Stock;
            product.Stock.PrimaryStockSource = StockSource.Eshop;
            product.Location = eshopProduct.Location;
            product.Image = eshopProduct.Image;
            product.DefaultImage = eshopProduct.DefaultImage;
            product.GrossWeight = eshopProduct.Weight;
            product.Height = eshopProduct.Height;
            product.Width = eshopProduct.Width;
            product.Depth = eshopProduct.Depth;
            product.AtypicalShipping = eshopProduct.AtypicalShipping;
        }
    }

    private static void MergeHistoryData(
        CatalogAggregate product,
        IDictionary<string, List<ConsumedMaterialRecord>> consumedMap,
        IDictionary<string, List<CatalogPurchaseRecord>> purchaseMap,
        IDictionary<string, List<CatalogManufactureRecord>> manufactureMap,
        IDictionary<string, List<StockTakingRecord>> stockTakingMap)
    {
        if (consumedMap.TryGetValue(product.ProductCode, out var consumed))
        {
            product.ConsumedHistory = consumed.ToList();
        }

        if (purchaseMap.TryGetValue(product.ProductCode, out var purchases))
        {
            product.PurchaseHistory = purchases.ToList();
        }

        if (manufactureMap.TryGetValue(product.ProductCode, out var manufactures))
        {
            product.ManufactureHistory = manufactures.ToList();
        }

        if (stockTakingMap.TryGetValue(product.ProductCode, out var stockTakings))
        {
            product.StockTakingHistory = stockTakings.OrderByDescending(o => o.Date).ToList();
        }
    }

    private static void MergeLots(CatalogAggregate product, IDictionary<string, List<CatalogLot>> lotsMap)
    {
        if (lotsMap.TryGetValue(product.ProductCode, out var lots))
        {
            product.Stock.Lots = lots.ToList();
        }
    }

    private static void MergePrices(
        CatalogAggregate product,
        IDictionary<string, ProductPriceEshop> eshopPriceMap,
        IDictionary<string, ProductPriceErp> erpPriceMap)
    {
        if (eshopPriceMap.TryGetValue(product.ProductCode, out var eshopPrice))
        {
            product.EshopPrice = eshopPrice;
        }

        if (erpPriceMap.TryGetValue(product.ProductCode, out var erpPrice))
        {
            product.ErpPrice = erpPrice;
        }
    }

    private static void MergeEshopUrl(CatalogAggregate product, IDictionary<string, string> eshopUrlMap)
    {
        if (eshopUrlMap.TryGetValue(product.ProductCode, out var eshopUrl))
        {
            product.Url = eshopUrl;
        }
    }

    private void MergeManufactureDifficultySettings(
        CatalogAggregate product,
        IDictionary<string, List<ManufactureDifficultySetting>> manufactureDifficultyData)
    {
        if (manufactureDifficultyData.TryGetValue(product.ProductCode, out var difficultySettings))
        {
            product.ManufactureDifficultySettings.Assign(difficultySettings.ToList(), _timeProvider.GetUtcNow().UtcDateTime);
        }
    }

    private static ProductType GetProductType(ErpStock s)
    {
        var type = (ProductType?)s.ProductTypeId ?? ProductType.UNDEFINED;

        if (type == ProductType.Product && (s.ProductCode.StartsWith("BAL") || s.ProductCode.StartsWith("SET")))
            return ProductType.Set;

        return type;
    }
}
