using Anela.Heblo.Application.Common;
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Attributes;
using Anela.Heblo.Domain.Features.Catalog.ConsumedMaterials;
using Anela.Heblo.Domain.Features.Catalog.Lots;
using Anela.Heblo.Domain.Features.Catalog.EshopUrl;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.Catalog.PurchaseHistory;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Manufacture;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

/// <summary>
/// Encapsulates all data refresh operations for the catalog cache.
/// Each method fetches fresh data from a source and updates the cache store.
/// </summary>
public sealed class CatalogDataRefreshService
{
    private readonly ICatalogSalesClient _salesClient;
    private readonly ICatalogSetPartsClient _setPartsClient;
    private readonly ICatalogAttributesClient _attributesClient;
    private readonly IEshopStockClient _eshopStockClient;
    private readonly IConsumedMaterialsClient _consumedMaterialClient;
    private readonly IPurchaseHistoryClient _purchaseHistoryClient;
    private readonly IErpStockClient _erpStockClient;
    private readonly ILotsClient _lotsClient;
    private readonly IProductPriceEshopClient _productPriceEshopClient;
    private readonly IProductPriceErpClient _productPriceErpClient;
    private readonly IProductEshopUrlClient _productEshopUrlClient;
    private readonly ICatalogTransportSource _transportSource;
    private readonly IStockTakingRepository _stockTakingRepository;
    private readonly ICatalogPurchaseSource _purchaseSource;
    private readonly ICatalogManufactureSource _manufactureSource;
    private readonly IManufactureDifficultyRepository _manufactureDifficultyRepository;
    private readonly ICatalogResilienceService _resilienceService;
    private readonly TimeProvider _timeProvider;
    private readonly IOptions<DataSourceOptions> _options;
    private readonly CatalogCacheStore _cacheStore;
    private readonly ILogger<CatalogDataRefreshService> _logger;

    public CatalogDataRefreshService(
        ICatalogSalesClient salesClient,
        ICatalogSetPartsClient setPartsClient,
        ICatalogAttributesClient attributesClient,
        IEshopStockClient eshopStockClient,
        IConsumedMaterialsClient consumedMaterialClient,
        IPurchaseHistoryClient purchaseHistoryClient,
        IErpStockClient erpStockClient,
        ILotsClient lotsClient,
        IProductPriceEshopClient productPriceEshopClient,
        IProductPriceErpClient productPriceErpClient,
        IProductEshopUrlClient productEshopUrlClient,
        ICatalogTransportSource transportSource,
        IStockTakingRepository stockTakingRepository,
        ICatalogPurchaseSource purchaseSource,
        ICatalogManufactureSource manufactureSource,
        IManufactureDifficultyRepository manufactureDifficultyRepository,
        ICatalogResilienceService resilienceService,
        TimeProvider timeProvider,
        IOptions<DataSourceOptions> options,
        CatalogCacheStore cacheStore,
        ILogger<CatalogDataRefreshService> logger)
    {
        _salesClient = salesClient ?? throw new ArgumentNullException(nameof(salesClient));
        _setPartsClient = setPartsClient ?? throw new ArgumentNullException(nameof(setPartsClient));
        _attributesClient = attributesClient ?? throw new ArgumentNullException(nameof(attributesClient));
        _eshopStockClient = eshopStockClient ?? throw new ArgumentNullException(nameof(eshopStockClient));
        _consumedMaterialClient = consumedMaterialClient ?? throw new ArgumentNullException(nameof(consumedMaterialClient));
        _purchaseHistoryClient = purchaseHistoryClient ?? throw new ArgumentNullException(nameof(purchaseHistoryClient));
        _erpStockClient = erpStockClient ?? throw new ArgumentNullException(nameof(erpStockClient));
        _lotsClient = lotsClient ?? throw new ArgumentNullException(nameof(lotsClient));
        _productPriceEshopClient = productPriceEshopClient ?? throw new ArgumentNullException(nameof(productPriceEshopClient));
        _productPriceErpClient = productPriceErpClient ?? throw new ArgumentNullException(nameof(productPriceErpClient));
        _productEshopUrlClient = productEshopUrlClient ?? throw new ArgumentNullException(nameof(productEshopUrlClient));
        _transportSource = transportSource ?? throw new ArgumentNullException(nameof(transportSource));
        _stockTakingRepository = stockTakingRepository ?? throw new ArgumentNullException(nameof(stockTakingRepository));
        _purchaseSource = purchaseSource ?? throw new ArgumentNullException(nameof(purchaseSource));
        _manufactureSource = manufactureSource ?? throw new ArgumentNullException(nameof(manufactureSource));
        _manufactureDifficultyRepository = manufactureDifficultyRepository ?? throw new ArgumentNullException(nameof(manufactureDifficultyRepository));
        _resilienceService = resilienceService ?? throw new ArgumentNullException(nameof(resilienceService));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _cacheStore = cacheStore ?? throw new ArgumentNullException(nameof(cacheStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task RefreshTransportData(CancellationToken ct)
    {
        var transportData = await _transportSource.GetProductsInTransportAsync(ct);
        _cacheStore.SetInTransportData(transportData);
    }

    public async Task RefreshManufacturedData(CancellationToken ct)
    {
        var manufacturedData = await _manufactureSource.GetManufacturedInventoryAsync(ct);
        _cacheStore.SetManufacturedData(manufacturedData);
    }

    public async Task RefreshReserveData(CancellationToken ct)
    {
        var reserveData = await _transportSource.GetProductsInReserveAsync(ct);
        _cacheStore.SetInReserveData(reserveData);

        var quarantineData = await _transportSource.GetProductsInQuarantineAsync(ct);
        _cacheStore.SetInQuarantineData(quarantineData);
    }

    public async Task RefreshOrderedData(CancellationToken ct)
    {
        var orderedData = await _purchaseSource.GetOrderedQuantitiesAsync(ct);
        _cacheStore.SetOrderedData(orderedData);
    }

    public async Task RefreshPlannedData(CancellationToken ct)
    {
        var plannedData = await _manufactureSource.GetPlannedQuantitiesAsync(ct);
        _cacheStore.SetPlannedData(plannedData);
    }

    public async Task RefreshSalesData(CancellationToken ct)
    {
        try
        {
            _cacheStore.SetSalesData(await _resilienceService.ExecuteWithResilienceAsync(
                async (cancellationToken) => await _salesClient.GetAsync(
                    _timeProvider.GetUtcNow().Date.AddDays(-1 * _options.Value.SalesHistoryDays),
                    _timeProvider.GetUtcNow().Date,
                    cancellationToken: cancellationToken),
                "RefreshSalesData", ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "RefreshSalesData failed after all retries — retaining stale cache. Items in cache: {Count}", _cacheStore.GetSalesData().Count);
        }
    }

    public async Task RefreshSetPartsData(CancellationToken ct)
    {
        try
        {
            var bundleCodes = _cacheStore.GetErpStockData()
                .Where(s => BundleProductRule.Resolve((ProductType?)s.ProductTypeId ?? ProductType.UNDEFINED, s.ProductCode) == ProductType.Set)
                .Select(s => s.ProductCode)
                .ToList();

            if (bundleCodes.Count == 0)
            {
                _logger.LogWarning(
                    "RefreshSetPartsData found no bundle-coded products in ERP stock — bundle sales expansion will be inactive. Retaining existing set-parts cache. Items in cache: {Count}",
                    _cacheStore.GetSetPartsData().Count);
                return;
            }

            var setParts = (IList<CatalogSetPart>)(await _resilienceService.ExecuteWithResilienceAsync(
                async (cancellationToken) => (IList<CatalogSetPart>)(await _setPartsClient.GetAsync(
                    bundleCodes,
                    cancellationToken)).ToList(),
                "RefreshSetPartsData", ct));

            _cacheStore.SetSetPartsData(setParts);

            _logger.LogInformation(
                "RefreshSetPartsData refreshed successfully: {BundleCount} bundles resolved, {PartCount} parts retrieved",
                bundleCodes.Count,
                setParts.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "RefreshSetPartsData failed after all retries — retaining stale cache. Items in cache: {Count}", _cacheStore.GetSetPartsData().Count);
        }
    }

    public async Task RefreshAttributesData(CancellationToken ct)
    {
        _cacheStore.SetCatalogAttributesData(await _resilienceService.ExecuteWithResilienceAsync(
            async (cancellationToken) => await _attributesClient.GetAttributesAsync(cancellationToken: cancellationToken),
            "RefreshAttributesData", ct));
    }

    public async Task RefreshErpStockData(CancellationToken ct)
    {
        _cacheStore.SetErpStockData(await _resilienceService.ExecuteWithResilienceAsync(
            async (cancellationToken) => (await _erpStockClient.ListAsync(cancellationToken)).ToList(),
            "RefreshErpStockData", ct));
    }

    public async Task RefreshEshopStockData(CancellationToken ct)
    {
        _cacheStore.SetEshopStockData(await _resilienceService.ExecuteWithResilienceAsync(
            async (cancellationToken) => (await _eshopStockClient.ListAsync(cancellationToken)).ToList(),
            "RefreshEshopStockData", ct));
    }

    public async Task RefreshPurchaseHistoryData(CancellationToken ct)
    {
        _cacheStore.SetPurchaseHistoryData((await _purchaseHistoryClient.GetHistoryAsync(null, _timeProvider.GetUtcNow().Date.AddDays(-1 * _options.Value.PurchaseHistoryDays), _timeProvider.GetUtcNow().Date, cancellationToken: ct))
            .ToList());
    }

    public async Task RefreshConsumedHistoryData(CancellationToken ct)
    {
        _cacheStore.SetConsumedData((await _consumedMaterialClient.GetConsumedAsync(_timeProvider.GetUtcNow().Date.AddDays(-1 * _options.Value.ConsumedHistoryDays), _timeProvider.GetUtcNow().Date, cancellationToken: ct))
            .ToList());
    }

    public async Task RefreshStockTakingData(CancellationToken ct)
    {
        _cacheStore.SetStockTakingData((await _stockTakingRepository.GetAllAsync(ct)).ToList());
    }

    public async Task RefreshLotsData(CancellationToken ct)
    {
        _cacheStore.SetLotsData((await _lotsClient.GetAsync(cancellationToken: ct)).ToList());
    }

    public async Task RefreshEshopPricesData(CancellationToken ct)
    {
        _cacheStore.SetEshopPriceData((await _productPriceEshopClient.GetAllAsync(ct)).ToList());
    }

    public async Task RefreshErpPricesData(CancellationToken ct)
    {
        _cacheStore.SetErpPriceData((await _productPriceErpClient.GetAllAsync(false, ct)).ToList());
    }

    public async Task RefreshEshopUrlData(CancellationToken ct)
    {
        _cacheStore.SetEshopUrlData((await _productEshopUrlClient.GetAllAsync(ct)).ToList());
    }

    public async Task RefreshManufactureDifficultySettingsData(string? product, CancellationToken ct)
    {
        var difficultySettings = await _manufactureDifficultyRepository.ListAsync(product, cancellationToken: ct);

        if (product == null) // All
        {
            _cacheStore.SetManufactureDifficultySettingsData(difficultySettings
                .GroupBy(h => h.ProductCode)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(h => h.ValidFrom ?? DateTime.MinValue).ToList()));
        }
        else
        {
            // Single product: copy-then-set the dictionary so InvalidateSourceData/SetLoadDateInCache
            // run through the same Set*Data plumbing every other refresh path uses.
            var existingDict = _cacheStore.GetManufactureDifficultySettingsData();
            var newDict = new Dictionary<string, List<ManufactureDifficultySetting>>(existingDict)
            {
                [product] = difficultySettings.ToList()
            };
            _cacheStore.SetManufactureDifficultySettingsData(newDict);

            // Update the live snapshot, if one exists, by swapping in a clone of the touched
            // product rather than mutating the shared aggregate a concurrent reader may hold.
            var current = _cacheStore.TryGetCurrent();
            var productAggregate = current?.SingleOrDefault(s => s.ProductCode == product);
            if (current != null && productAggregate != null)
            {
                var updated = current.Select(p =>
                {
                    if (p != productAggregate)
                    {
                        return p;
                    }

                    var clone = p.Clone();
                    clone.ManufactureDifficultySettings.Assign(difficultySettings, _timeProvider.GetUtcNow().UtcDateTime);
                    return clone;
                }).ToList();

                await _cacheStore.ReplaceCacheAtomicallyAsync(updated);
            }
        }
    }

    public async Task RefreshManufactureHistoryData(CancellationToken ct)
    {
        _cacheStore.SetManufactureHistoryData((await _manufactureSource.GetManufactureHistoryAsync(
            _timeProvider.GetUtcNow().Date.AddDays(-1 * _options.Value.ManufactureHistoryDays),
            _timeProvider.GetUtcNow().Date,
            ct)).ToList());
    }

    public async Task RefreshManufactureCostData(CancellationToken ct)
    {
        // Add ManufactureHistory data
        var manufactureMap = _cacheStore.GetManufactureHistoryData()
            .GroupBy(p => p.ProductCode)
            .ToDictionary(k => k.Key, v => v.ToList());

        var catalogData = _cacheStore.GetCatalogData();
        var updated = (catalogData ?? []).Select(product =>
        {
            if (!manufactureMap.TryGetValue(product.ProductCode, out var manufactures))
            {
                return product;
            }

            var clone = product.Clone();
            clone.ManufactureHistory = manufactures.ToList();
            return clone;
        }).ToList();

        await _cacheStore.ReplaceCacheAtomicallyAsync(updated);
    }
}
