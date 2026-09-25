using Anela.Heblo.Application.Common;
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.ConsumedMaterials;
using Anela.Heblo.Domain.Features.Catalog.PurchaseHistory;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

/// <summary>
/// Refreshes time-windowed catalog history: sales, set-parts composition, purchase history,
/// consumed-material history, and manufacture history.
/// </summary>
public sealed class CatalogHistoryRefreshService
{
    private readonly ICatalogSalesClient _salesClient;
    private readonly ICatalogSetPartsClient _setPartsClient;
    private readonly IPurchaseHistoryClient _purchaseHistoryClient;
    private readonly IConsumedMaterialsClient _consumedMaterialClient;
    private readonly ICatalogManufactureSource _manufactureSource;
    private readonly ICatalogResilienceService _resilienceService;
    private readonly TimeProvider _timeProvider;
    private readonly IOptions<DataSourceOptions> _options;
    private readonly CatalogCacheStore _cacheStore;
    private readonly ILogger<CatalogHistoryRefreshService> _logger;

    public CatalogHistoryRefreshService(
        ICatalogSalesClient salesClient,
        ICatalogSetPartsClient setPartsClient,
        IPurchaseHistoryClient purchaseHistoryClient,
        IConsumedMaterialsClient consumedMaterialClient,
        ICatalogManufactureSource manufactureSource,
        ICatalogResilienceService resilienceService,
        TimeProvider timeProvider,
        IOptions<DataSourceOptions> options,
        CatalogCacheStore cacheStore,
        ILogger<CatalogHistoryRefreshService> logger)
    {
        _salesClient = salesClient ?? throw new ArgumentNullException(nameof(salesClient));
        _setPartsClient = setPartsClient ?? throw new ArgumentNullException(nameof(setPartsClient));
        _purchaseHistoryClient = purchaseHistoryClient ?? throw new ArgumentNullException(nameof(purchaseHistoryClient));
        _consumedMaterialClient = consumedMaterialClient ?? throw new ArgumentNullException(nameof(consumedMaterialClient));
        _manufactureSource = manufactureSource ?? throw new ArgumentNullException(nameof(manufactureSource));
        _resilienceService = resilienceService ?? throw new ArgumentNullException(nameof(resilienceService));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _cacheStore = cacheStore ?? throw new ArgumentNullException(nameof(cacheStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

    /// <summary>
    /// Reads the ERP stock cache to decide which products are bundles, so it must run after
    /// RefreshErpStockData has populated it. A hydration tier runs its tasks concurrently, so this
    /// task is configured one tier later than RefreshErpStockData rather than relying on ordering
    /// within a tier — otherwise a cold start can leave bundle expansion inactive until the next
    /// scheduled run.
    /// </summary>
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

            var (setParts, failedCount) = await FetchSetPartsPerBundleAsync(bundleCodes, ct);

            if (failedCount == bundleCodes.Count)
            {
                _logger.LogWarning(
                    "RefreshSetPartsData could not fetch any of {BundleCount} bundles — retaining stale cache. Items in cache: {Count}",
                    bundleCodes.Count,
                    _cacheStore.GetSetPartsData().Count);
                return;
            }

            _cacheStore.SetSetPartsData(setParts);

            _logger.LogInformation(
                "RefreshSetPartsData refreshed successfully: {BundleCount} bundles resolved ({FailedCount} failed), {PartCount} parts retrieved",
                bundleCodes.Count,
                failedCount,
                setParts.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "RefreshSetPartsData failed after all retries — retaining stale cache. Items in cache: {Count}", _cacheStore.GetSetPartsData().Count);
        }
    }

    /// <summary>
    /// Fetches set parts one bundle at a time. Flexi has no batch endpoint for bundle composition,
    /// so a single resilience execution around the whole loop would spend one 30s timeout budget on
    /// N sequential calls and start failing outright once the bundle count grows. Per-bundle
    /// execution also keeps one broken bundle definition from blocking every other bundle.
    /// </summary>
    private async Task<(IList<CatalogSetPart> Parts, int FailedCount)> FetchSetPartsPerBundleAsync(
        IReadOnlyList<string> bundleCodes,
        CancellationToken ct)
    {
        var parts = new List<CatalogSetPart>();
        var failedCount = 0;

        foreach (var bundleCode in bundleCodes)
        {
            try
            {
                var bundleParts = await _resilienceService.ExecuteWithResilienceAsync(
                    async (cancellationToken) => (IList<CatalogSetPart>)(await _setPartsClient.GetAsync(
                        new[] { bundleCode },
                        cancellationToken)).ToList(),
                    "RefreshSetPartsData", ct);

                parts.AddRange(bundleParts);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failedCount++;
                _logger.LogWarning(
                    ex,
                    "RefreshSetPartsData could not fetch bundle {SetCode} — its sales will not be expanded this cycle.",
                    bundleCode);
            }
        }

        return (parts, failedCount);
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

    public async Task RefreshManufactureHistoryData(CancellationToken ct)
    {
        _cacheStore.SetManufactureHistoryData((await _manufactureSource.GetManufactureHistoryAsync(
            _timeProvider.GetUtcNow().Date.AddDays(-1 * _options.Value.ManufactureHistoryDays),
            _timeProvider.GetUtcNow().Date,
            ct)).ToList());
    }
}
