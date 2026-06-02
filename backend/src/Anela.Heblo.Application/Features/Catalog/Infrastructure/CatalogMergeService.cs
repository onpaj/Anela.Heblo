using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

public sealed class CatalogMergeService
{
    private readonly CatalogCacheStore _cacheStore;
    private readonly ICatalogMergeScheduler _mergeScheduler;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CatalogMergeService> _logger;

    public CatalogMergeService(
        CatalogCacheStore cacheStore,
        ICatalogMergeScheduler mergeScheduler,
        TimeProvider timeProvider,
        ILogger<CatalogMergeService> logger)
    {
        _cacheStore = cacheStore;
        _mergeScheduler = mergeScheduler;
        _timeProvider = timeProvider;
        _logger = logger;
    }

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

    public async Task<List<CatalogAggregate>> ExecutePriorityMergeAsync()
    {
        _logger.LogInformation("Executing priority merge - no cache available");
        var newCatalogData = await Task.Run(() => Merge());
        await _cacheStore.ReplaceCacheAtomicallyAsync(newCatalogData);
        return newCatalogData;
    }

    internal List<CatalogAggregate> Merge()
    {
        var current = _cacheStore.TryGetCurrent();
        var erpStockList = _cacheStore.GetErpStockData();

        List<CatalogAggregate> products = (current == null || current.Count == 0)
            ? erpStockList.Select(s => new CatalogAggregate { ProductCode = s.ProductCode }).ToList()
            : current;

        var attributesMap = _cacheStore.GetCatalogAttributesData().ToDictionary(k => k.ProductCode, v => v);
        var eshopProductsMap = _cacheStore.GetEshopStockData().ToDictionary(k => k.Code, v => v);
        var erpProductsMap = erpStockList.ToDictionary(k => k.ProductCode, v => v);
        var consumedMap = _cacheStore.GetConsumedData().GroupBy(p => p.ProductCode).ToDictionary(k => k.Key, v => v.ToList());
        var purchaseMap = _cacheStore.GetPurchaseHistoryData().GroupBy(p => p.ProductCode).ToDictionary(k => k.Key, v => v.ToList());
        var manufactureMap = _cacheStore.GetManufactureHistoryData().GroupBy(p => p.ProductCode).ToDictionary(k => k.Key, v => v.ToList());
        var stockTakingMap = _cacheStore.GetStockTakingData().GroupBy(p => p.Code).ToDictionary(k => k.Key, v => v.ToList());
        var lotsMap = _cacheStore.GetLotsData().GroupBy(p => p.ProductCode).ToDictionary(k => k.Key, v => v.ToList());
        var eshopPriceMap = _cacheStore.GetEshopPriceData().ToDictionary(k => k.ProductCode, v => v);
        var erpPriceMap = _cacheStore.GetErpPriceData().ToDictionary(k => k.ProductCode, v => v);
        var eshopUrlMap = _cacheStore.GetEshopUrlData().ToDictionary(k => k.ProductCode, v => v.Url);
        var salesData = _cacheStore.GetSalesData();
        var transportMap = _cacheStore.GetInTransportData();
        var manufacturedMap = _cacheStore.GetManufacturedData();
        var reserveMap = _cacheStore.GetInReserveData();
        var quarantineMap = _cacheStore.GetInQuarantineData();
        var orderedMap = _cacheStore.GetOrderedData();
        var plannedMap = _cacheStore.GetPlannedData();
        var difficultyMap = _cacheStore.GetManufactureDifficultySettingsData();

        foreach (var product in products)
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

            product.SalesHistory = salesData.Where(w => w.ProductCode == product.ProductCode).ToList();

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

            product.Stock.Transport = transportMap.ContainsKey(product.ProductCode) ? transportMap[product.ProductCode] : 0;
            product.Stock.Manufactured = manufacturedMap.ContainsKey(product.ProductCode) ? manufacturedMap[product.ProductCode] : 0;
            product.Stock.Reserve = reserveMap.ContainsKey(product.ProductCode) ? reserveMap[product.ProductCode] : 0;
            product.Stock.Quarantine = quarantineMap.ContainsKey(product.ProductCode) ? quarantineMap[product.ProductCode] : 0;
            product.Stock.Ordered = orderedMap.ContainsKey(product.ProductCode) ? orderedMap[product.ProductCode] : 0;
            product.Stock.Planned = plannedMap.ContainsKey(product.ProductCode) ? plannedMap[product.ProductCode] : 0;

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

            if (consumedMap.TryGetValue(product.ProductCode, out var consumed))
                product.ConsumedHistory = consumed.ToList();

            if (purchaseMap.TryGetValue(product.ProductCode, out var purchases))
                product.PurchaseHistory = purchases.ToList();

            if (manufactureMap.TryGetValue(product.ProductCode, out var manufactures))
                product.ManufactureHistory = manufactures.ToList();

            if (stockTakingMap.TryGetValue(product.ProductCode, out var stockTakings))
                product.StockTakingHistory = stockTakings.OrderByDescending(o => o.Date).ToList();

            if (lotsMap.TryGetValue(product.ProductCode, out var lots))
                product.Stock.Lots = lots.ToList();

            if (eshopPriceMap.TryGetValue(product.ProductCode, out var eshopPrice))
                product.EshopPrice = eshopPrice;

            if (erpPriceMap.TryGetValue(product.ProductCode, out var erpPrice))
                product.ErpPrice = erpPrice;

            if (eshopUrlMap.TryGetValue(product.ProductCode, out var eshopUrl))
                product.Url = eshopUrl;

            if (difficultyMap.TryGetValue(product.ProductCode, out var difficultySettings))
                product.ManufactureDifficultySettings.Assign(difficultySettings.ToList(), _timeProvider.GetUtcNow().UtcDateTime);
        }

        _cacheStore.SetLastMergeDateTime();
        return products.ToList();
    }

    private static ProductType GetProductType(ErpStock s)
    {
        var type = (ProductType?)s.ProductTypeId ?? ProductType.UNDEFINED;
        if (type == ProductType.Product && (s.ProductCode.StartsWith("BAL") || s.ProductCode.StartsWith("SET")))
            return ProductType.Set;
        return type;
    }
}
