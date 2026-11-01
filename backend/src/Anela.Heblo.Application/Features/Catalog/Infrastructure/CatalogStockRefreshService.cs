using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Domain.Features.Catalog.Eshop;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

/// <summary>
/// Refreshes current stock/inventory position data: ERP stock, eshop stock, in-transport,
/// in-reserve/in-quarantine, ordered quantities, and manufactured/planned inventory.
/// </summary>
public sealed class CatalogStockRefreshService
{
    private readonly IErpStockClient _erpStockClient;
    // One sweep at a time process-wide: two parallel sweeps would double the Shoptet request rate.
    private static readonly SemaphoreSlim EshopProductRefreshGate = new(1, 1);

    private readonly IEshopStockClient _eshopStockClient;
    private readonly IEshopProductClient _eshopProductClient;
    private readonly ICatalogTransportSource _transportSource;
    private readonly ICatalogPurchaseSource _purchaseSource;
    private readonly ICatalogManufactureSource _manufactureSource;
    private readonly ICatalogResilienceService _resilienceService;
    private readonly CatalogCacheStore _cacheStore;
    private readonly ILogger<CatalogStockRefreshService> _logger;

    public CatalogStockRefreshService(
        IErpStockClient erpStockClient,
        IEshopStockClient eshopStockClient,
        IEshopProductClient eshopProductClient,
        ICatalogTransportSource transportSource,
        ICatalogPurchaseSource purchaseSource,
        ICatalogManufactureSource manufactureSource,
        ICatalogResilienceService resilienceService,
        CatalogCacheStore cacheStore,
        ILogger<CatalogStockRefreshService> logger)
    {
        _erpStockClient = erpStockClient ?? throw new ArgumentNullException(nameof(erpStockClient));
        _eshopStockClient = eshopStockClient ?? throw new ArgumentNullException(nameof(eshopStockClient));
        _eshopProductClient = eshopProductClient ?? throw new ArgumentNullException(nameof(eshopProductClient));
        _transportSource = transportSource ?? throw new ArgumentNullException(nameof(transportSource));
        _purchaseSource = purchaseSource ?? throw new ArgumentNullException(nameof(purchaseSource));
        _manufactureSource = manufactureSource ?? throw new ArgumentNullException(nameof(manufactureSource));
        _resilienceService = resilienceService ?? throw new ArgumentNullException(nameof(resilienceService));
        _cacheStore = cacheStore ?? throw new ArgumentNullException(nameof(cacheStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

    /// <summary>
    /// Sweeps Shoptet product info (~220 sequential calls, ~70 s). Deliberately NOT wrapped in
    /// ICatalogResilienceService: its 30 s per-operation timeout would cancel every sweep and its
    /// retry would restart the whole sweep. The client retries individual calls itself and throws on
    /// any failure, so the cache is only replaced by a complete list.
    /// </summary>
    public async Task RefreshEshopProductData(CancellationToken ct)
    {
        if (!await EshopProductRefreshGate.WaitAsync(0, ct))
        {
            _logger.LogInformation("Shoptet product refresh already running; skipping");
            return;
        }

        try
        {
            var products = await _eshopProductClient.ListAsync(ct);
            _cacheStore.SetEshopProductData(products.ToList());
        }
        finally
        {
            EshopProductRefreshGate.Release();
        }
    }

    public async Task RefreshTransportData(CancellationToken ct)
    {
        var transportData = await _transportSource.GetProductsInTransportAsync(ct);
        _cacheStore.SetInTransportData(transportData);
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

    public async Task RefreshManufacturedData(CancellationToken ct)
    {
        var manufacturedData = await _manufactureSource.GetManufacturedInventoryAsync(ct);
        _cacheStore.SetManufacturedData(manufacturedData);
    }

    public async Task RefreshPlannedData(CancellationToken ct)
    {
        var plannedData = await _manufactureSource.GetPlannedQuantitiesAsync(ct);
        _cacheStore.SetPlannedData(plannedData);
    }
}
