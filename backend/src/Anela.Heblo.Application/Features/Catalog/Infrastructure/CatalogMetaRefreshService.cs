using Anela.Heblo.Domain.Features.Catalog.Attributes;
using Anela.Heblo.Domain.Features.Catalog.EshopUrl;
using Anela.Heblo.Domain.Features.Catalog.Lots;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

/// <summary>
/// Refreshes catalog metadata: product attributes, lots, eshop/ERP prices, and eshop URLs.
/// </summary>
public sealed class CatalogMetaRefreshService
{
    private readonly ICatalogAttributesClient _attributesClient;
    private readonly ILotsClient _lotsClient;
    private readonly IProductPriceEshopClient _productPriceEshopClient;
    private readonly IProductPriceErpClient _productPriceErpClient;
    private readonly IProductEshopUrlClient _productEshopUrlClient;
    private readonly ICatalogResilienceService _resilienceService;
    private readonly CatalogCacheStore _cacheStore;
    private readonly ILogger<CatalogMetaRefreshService> _logger;

    public CatalogMetaRefreshService(
        ICatalogAttributesClient attributesClient,
        ILotsClient lotsClient,
        IProductPriceEshopClient productPriceEshopClient,
        IProductPriceErpClient productPriceErpClient,
        IProductEshopUrlClient productEshopUrlClient,
        ICatalogResilienceService resilienceService,
        CatalogCacheStore cacheStore,
        ILogger<CatalogMetaRefreshService> logger)
    {
        _attributesClient = attributesClient ?? throw new ArgumentNullException(nameof(attributesClient));
        _lotsClient = lotsClient ?? throw new ArgumentNullException(nameof(lotsClient));
        _productPriceEshopClient = productPriceEshopClient ?? throw new ArgumentNullException(nameof(productPriceEshopClient));
        _productPriceErpClient = productPriceErpClient ?? throw new ArgumentNullException(nameof(productPriceErpClient));
        _productEshopUrlClient = productEshopUrlClient ?? throw new ArgumentNullException(nameof(productEshopUrlClient));
        _resilienceService = resilienceService ?? throw new ArgumentNullException(nameof(resilienceService));
        _cacheStore = cacheStore ?? throw new ArgumentNullException(nameof(cacheStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task RefreshAttributesData(CancellationToken ct)
    {
        _cacheStore.SetCatalogAttributesData(await _resilienceService.ExecuteWithResilienceAsync(
            async (cancellationToken) => await _attributesClient.GetAttributesAsync(cancellationToken: cancellationToken),
            "RefreshAttributesData", ct));
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
}
