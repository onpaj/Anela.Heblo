using Anela.Heblo.Application.Common;
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Attributes;
using Anela.Heblo.Domain.Features.Catalog.ConsumedMaterials;
using Anela.Heblo.Domain.Features.Catalog.EshopUrl;
using Anela.Heblo.Domain.Features.Catalog.Lots;
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
        _salesClient = salesClient;
        _attributesClient = attributesClient;
        _eshopStockClient = eshopStockClient;
        _consumedMaterialClient = consumedMaterialClient;
        _purchaseHistoryClient = purchaseHistoryClient;
        _erpStockClient = erpStockClient;
        _lotsClient = lotsClient;
        _productPriceEshopClient = productPriceEshopClient;
        _productPriceErpClient = productPriceErpClient;
        _productEshopUrlClient = productEshopUrlClient;
        _transportBoxRepository = transportBoxRepository;
        _stockTakingRepository = stockTakingRepository;
        _purchaseOrderRepository = purchaseOrderRepository;
        _manufactureOrderRepository = manufactureOrderRepository;
        _manufactureHistoryClient = manufactureHistoryClient;
        _manufactureDifficultyRepository = manufactureDifficultyRepository;
        _manufacturedInventoryRepository = manufacturedInventoryRepository;
        _resilienceService = resilienceService;
        _timeProvider = timeProvider;
        _options = options;
        _cacheStore = cacheStore;
        _logger = logger;
    }

    public async Task RefreshTransportData(CancellationToken ct)
        => _cacheStore.SetInTransportData(await GetProductsInTransport(ct));

    public async Task RefreshManufacturedData(CancellationToken ct)
        => _cacheStore.SetManufacturedData(await _manufacturedInventoryRepository.GetTotalAmountByProductCodeAsync(ct));

    public async Task RefreshReserveData(CancellationToken ct)
    {
        _cacheStore.SetInReserveData(await GetProductsInReserve(ct));
        _cacheStore.SetInQuarantineData(await GetProductsInQuarantine(ct));
    }

    public async Task RefreshOrderedData(CancellationToken ct)
        => _cacheStore.SetOrderedData(await GetProductsOrdered(ct));

    public async Task RefreshPlannedData(CancellationToken ct)
        => _cacheStore.SetPlannedData(await GetProductsPlanned(ct));

    public async Task RefreshSalesData(CancellationToken ct)
    {
        try
        {
            var sales = await _resilienceService.ExecuteWithResilienceAsync(
                async (cancellationToken) => await _salesClient.GetAsync(
                    _timeProvider.GetUtcNow().Date.AddDays(-1 * _options.Value.SalesHistoryDays),
                    _timeProvider.GetUtcNow().Date,
                    cancellationToken: cancellationToken),
                "RefreshSalesData", ct);
            _cacheStore.SetSalesData(sales);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "RefreshSalesData failed after all retries — retaining stale cache. Items in cache: {Count}",
                _cacheStore.GetSalesData().Count);
        }
    }

    public async Task RefreshAttributesData(CancellationToken ct)
    {
        var attributes = await _resilienceService.ExecuteWithResilienceAsync(
            async (cancellationToken) => await _attributesClient.GetAttributesAsync(cancellationToken: cancellationToken),
            "RefreshAttributesData", ct);
        _cacheStore.SetCatalogAttributesData(attributes);
    }

    public async Task RefreshErpStockData(CancellationToken ct)
    {
        var stock = await _resilienceService.ExecuteWithResilienceAsync(
            async (cancellationToken) => (await _erpStockClient.ListAsync(cancellationToken)).ToList(),
            "RefreshErpStockData", ct);
        _cacheStore.SetErpStockData(stock);
    }

    public async Task RefreshEshopStockData(CancellationToken ct)
    {
        var stock = await _resilienceService.ExecuteWithResilienceAsync(
            async (cancellationToken) => (await _eshopStockClient.ListAsync(cancellationToken)).ToList(),
            "RefreshEshopStockData", ct);
        _cacheStore.SetEshopStockData(stock);
    }

    public async Task RefreshPurchaseHistoryData(CancellationToken ct)
    {
        var data = (await _purchaseHistoryClient.GetHistoryAsync(
            null,
            _timeProvider.GetUtcNow().Date.AddDays(-1 * _options.Value.PurchaseHistoryDays),
            _timeProvider.GetUtcNow().Date,
            cancellationToken: ct)).ToList();
        _cacheStore.SetPurchaseHistoryData(data);
    }

    public async Task RefreshConsumedHistoryData(CancellationToken ct)
    {
        var data = (await _consumedMaterialClient.GetConsumedAsync(
            _timeProvider.GetUtcNow().Date.AddDays(-1 * _options.Value.ConsumedHistoryDays),
            _timeProvider.GetUtcNow().Date,
            cancellationToken: ct)).ToList();
        _cacheStore.SetConsumedData(data);
    }

    public async Task RefreshStockTakingData(CancellationToken ct)
        => _cacheStore.SetStockTakingData((await _stockTakingRepository.GetAllAsync(ct)).ToList());

    public async Task RefreshLotsData(CancellationToken ct)
        => _cacheStore.SetLotsData((await _lotsClient.GetAsync(cancellationToken: ct)).ToList());

    public async Task RefreshEshopPricesData(CancellationToken ct)
        => _cacheStore.SetEshopPriceData((await _productPriceEshopClient.GetAllAsync(ct)).ToList());

    public async Task RefreshErpPricesData(CancellationToken ct)
        => _cacheStore.SetErpPriceData((await _productPriceErpClient.GetAllAsync(false, ct)).ToList());

    public async Task RefreshEshopUrlData(CancellationToken ct)
        => _cacheStore.SetEshopUrlData((await _productEshopUrlClient.GetAllAsync(ct)).ToList());

    public async Task RefreshManufactureDifficultySettingsData(string? product, CancellationToken ct)
    {
        var difficultySettings = await _manufactureDifficultyRepository.ListAsync(product, cancellationToken: ct);

        if (product == null)
        {
            _cacheStore.SetManufactureDifficultySettingsData(difficultySettings
                .GroupBy(h => h.ProductCode)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(h => h.ValidFrom ?? DateTime.MinValue).ToList()));
        }
        else
        {
            var existing = _cacheStore.GetManufactureDifficultySettingsData();
            existing[product] = difficultySettings;
            _cacheStore.SetManufactureDifficultySettingsData(existing);

            var current = _cacheStore.TryGetCurrent();
            current?.SingleOrDefault(s => s.ProductCode == product)?
                .ManufactureDifficultySettings.Assign(difficultySettings, _timeProvider.GetUtcNow().UtcDateTime);
        }
    }

    public async Task RefreshManufactureHistoryData(CancellationToken ct)
    {
        var history = (await _manufactureHistoryClient.GetHistoryAsync(
            _timeProvider.GetUtcNow().Date.AddDays(-1 * _options.Value.ManufactureHistoryDays),
            _timeProvider.GetUtcNow().Date,
            cancellationToken: ct)).ToList();
        _cacheStore.SetManufactureHistoryData(history);
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

    private Task<Dictionary<string, decimal>> GetProductsOrdered(CancellationToken ct)
        => _purchaseOrderRepository.GetOrderedQuantitiesAsync(ct);

    private Task<Dictionary<string, decimal>> GetProductsPlanned(CancellationToken ct)
        => _manufactureOrderRepository.GetPlannedQuantitiesAsync(ct);
}
