using Anela.Heblo.Application.Common;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Attributes;
using Anela.Heblo.Domain.Features.Catalog.ConsumedMaterials;
using Anela.Heblo.Domain.Features.Catalog.Lots;
using Anela.Heblo.Domain.Features.Catalog.EshopUrl;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.Catalog.PurchaseHistory;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Manufacture.Inventory;
using Anela.Heblo.Domain.Features.Purchase;
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
    private readonly ICatalogAttributesClient _attributesClient;
    private readonly IEshopStockClient _eshopStockClient;
    private readonly IConsumedMaterialsClient _consumedMaterialClient;
    private readonly IPurchaseHistoryClient _purchaseHistoryClient;
    private readonly IErpStockClient _erpStockClient;
    private readonly ILotsClient _lotsClient;
    private readonly IProductPriceEshopClient _productPriceEshopClient;
    private readonly IProductPriceErpClient _productPriceErpClient;
    private readonly IProductEshopUrlClient _productEshopUrlClient;
    private readonly ITransportBoxRepository _transportBoxRepository;
    private readonly IStockTakingRepository _stockTakingRepository;
    private readonly IPurchaseOrderRepository _purchaseOrderRepository;
    private readonly IManufactureOrderRepository _manufactureOrderRepository;
    private readonly IManufactureHistoryClient _manufactureHistoryClient;
    private readonly IManufactureDifficultyRepository _manufactureDifficultyRepository;
    private readonly IManufacturedProductInventoryRepository _manufacturedInventoryRepository;
    private readonly ICatalogResilienceService _resilienceService;
    private readonly TimeProvider _timeProvider;
    private readonly IOptions<DataSourceOptions> _options;
    private readonly CatalogCacheStore _cacheStore;
    private readonly ILogger<CatalogDataRefreshService> _logger;

    public CatalogDataRefreshService(
        ICatalogSalesClient salesClient,
        ICatalogAttributesClient attributesClient,
        IEshopStockClient eshopStockClient,
        IConsumedMaterialsClient consumedMaterialClient,
        IPurchaseHistoryClient purchaseHistoryClient,
        IErpStockClient erpStockClient,
        ILotsClient lotsClient,
        IProductPriceEshopClient productPriceEshopClient,
        IProductPriceErpClient productPriceErpClient,
        IProductEshopUrlClient productEshopUrlClient,
        ITransportBoxRepository transportBoxRepository,
        IStockTakingRepository stockTakingRepository,
        IPurchaseOrderRepository purchaseOrderRepository,
        IManufactureOrderRepository manufactureOrderRepository,
        IManufactureHistoryClient manufactureHistoryClient,
        IManufactureDifficultyRepository manufactureDifficultyRepository,
        IManufacturedProductInventoryRepository manufacturedInventoryRepository,
        ICatalogResilienceService resilienceService,
        TimeProvider timeProvider,
        IOptions<DataSourceOptions> options,
        CatalogCacheStore cacheStore,
        ILogger<CatalogDataRefreshService> logger)
    {
        _salesClient = salesClient ?? throw new ArgumentNullException(nameof(salesClient));
        _attributesClient = attributesClient ?? throw new ArgumentNullException(nameof(attributesClient));
        _eshopStockClient = eshopStockClient ?? throw new ArgumentNullException(nameof(eshopStockClient));
        _consumedMaterialClient = consumedMaterialClient ?? throw new ArgumentNullException(nameof(consumedMaterialClient));
        _purchaseHistoryClient = purchaseHistoryClient ?? throw new ArgumentNullException(nameof(purchaseHistoryClient));
        _erpStockClient = erpStockClient ?? throw new ArgumentNullException(nameof(erpStockClient));
        _lotsClient = lotsClient ?? throw new ArgumentNullException(nameof(lotsClient));
        _productPriceEshopClient = productPriceEshopClient ?? throw new ArgumentNullException(nameof(productPriceEshopClient));
        _productPriceErpClient = productPriceErpClient ?? throw new ArgumentNullException(nameof(productPriceErpClient));
        _productEshopUrlClient = productEshopUrlClient ?? throw new ArgumentNullException(nameof(productEshopUrlClient));
        _transportBoxRepository = transportBoxRepository ?? throw new ArgumentNullException(nameof(transportBoxRepository));
        _stockTakingRepository = stockTakingRepository ?? throw new ArgumentNullException(nameof(stockTakingRepository));
        _purchaseOrderRepository = purchaseOrderRepository ?? throw new ArgumentNullException(nameof(purchaseOrderRepository));
        _manufactureOrderRepository = manufactureOrderRepository ?? throw new ArgumentNullException(nameof(manufactureOrderRepository));
        _manufactureHistoryClient = manufactureHistoryClient ?? throw new ArgumentNullException(nameof(manufactureHistoryClient));
        _manufactureDifficultyRepository = manufactureDifficultyRepository ?? throw new ArgumentNullException(nameof(manufactureDifficultyRepository));
        _manufacturedInventoryRepository = manufacturedInventoryRepository ?? throw new ArgumentNullException(nameof(manufacturedInventoryRepository));
        _resilienceService = resilienceService ?? throw new ArgumentNullException(nameof(resilienceService));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _cacheStore = cacheStore ?? throw new ArgumentNullException(nameof(cacheStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task RefreshTransportData(CancellationToken ct)
    {
        var transportData = await GetProductsInTransport(ct);
        _cacheStore.SetInTransportData(transportData);
    }

    public async Task RefreshManufacturedData(CancellationToken ct)
    {
        var manufacturedData = await _manufacturedInventoryRepository.GetTotalAmountByProductCodeAsync(ct);
        _cacheStore.SetManufacturedData(manufacturedData);
    }

    public async Task RefreshReserveData(CancellationToken ct)
    {
        var reserveData = await GetProductsInReserve(ct);
        _cacheStore.SetInReserveData(reserveData);

        var quarantineData = await GetProductsInQuarantine(ct);
        _cacheStore.SetInQuarantineData(quarantineData);
    }

    public async Task RefreshOrderedData(CancellationToken ct)
    {
        var orderedData = await GetProductsOrdered(ct);
        _cacheStore.SetOrderedData(orderedData);
    }

    public async Task RefreshPlannedData(CancellationToken ct)
    {
        var plannedData = await GetProductsPlanned(ct);
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
            // Single product - mutate the existing dictionary entry
            var existingDict = _cacheStore.GetManufactureDifficultySettingsData();
            existingDict[product] = difficultySettings.ToList();

            // Update live aggregate too
            var current = _cacheStore.TryGetCurrent();
            var productAggregate = current?.SingleOrDefault(s => s.ProductCode == product);
            if (productAggregate != null)
            {
                productAggregate.ManufactureDifficultySettings.Assign(difficultySettings, _timeProvider.GetUtcNow().UtcDateTime);
            }
        }
    }

    public async Task RefreshManufactureHistoryData(CancellationToken ct)
    {
        _cacheStore.SetManufactureHistoryData((await _manufactureHistoryClient.GetHistoryAsync(_timeProvider.GetUtcNow().Date.AddDays(-1 * _options.Value.ManufactureHistoryDays), _timeProvider.GetUtcNow().Date, cancellationToken: ct))
            .ToList());
    }

    public async Task RefreshManufactureCostData(CancellationToken ct)
    {
        // Add ManufactureHistory data
        var manufactureMap = _cacheStore.GetManufactureHistoryData()
            .GroupBy(p => p.ProductCode)
            .ToDictionary(k => k.Key, v => v.ToList());

        var catalogData = _cacheStore.GetCatalogData();
        foreach (var product in catalogData)
        {
            if (manufactureMap.TryGetValue(product.ProductCode, out var manufactures))
            {
                product.ManufactureHistory = manufactures.ToList();
            }
        }
    }

    private async Task<Dictionary<string, int>> GetProductsInTransport(CancellationToken ct)
    {
        var boxes = await _transportBoxRepository.FindAsync(TransportBox.IsInTransportPredicate, includeDetails: true, cancellationToken: ct);
        return boxes.SelectMany(s => s.Items)
            .GroupBy(g => g.ProductCode)
            .ToDictionary(k => k.Key, v => v.Sum(s => (int)s.Amount));
    }

    private async Task<Dictionary<string, int>> GetProductsInReserve(CancellationToken ct)
    {
        var boxes = await _transportBoxRepository.FindAsync(TransportBox.IsInReservePredicate, includeDetails: true, cancellationToken: ct);
        return boxes.SelectMany(s => s.Items)
            .GroupBy(g => g.ProductCode)
            .ToDictionary(k => k.Key, v => v.Sum(s => (int)s.Amount));
    }

    private async Task<Dictionary<string, int>> GetProductsInQuarantine(CancellationToken ct)
    {
        var boxes = await _transportBoxRepository.FindAsync(TransportBox.IsInQuarantinePredicate, includeDetails: true, cancellationToken: ct);
        return boxes.SelectMany(s => s.Items)
            .GroupBy(g => g.ProductCode)
            .ToDictionary(k => k.Key, v => v.Sum(s => (int)s.Amount));
    }

    private async Task<Dictionary<string, decimal>> GetProductsOrdered(CancellationToken ct)
    {
        return await _purchaseOrderRepository.GetOrderedQuantitiesAsync(ct);
    }

    private async Task<Dictionary<string, decimal>> GetProductsPlanned(CancellationToken ct)
    {
        return await _manufactureOrderRepository.GetPlannedQuantitiesAsync(ct);
    }
}
