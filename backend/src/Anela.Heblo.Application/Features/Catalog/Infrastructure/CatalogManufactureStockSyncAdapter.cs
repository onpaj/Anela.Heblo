using Anela.Heblo.Application.Features.Manufacture.Contracts;
using Anela.Heblo.Domain.Features.Catalog.Lots;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

/// <summary>
/// Catalog-side adapter that implements the Manufacture-owned IManufactureCatalogStockSync
/// contract. Refreshes only the affected product instead of triggering a full catalog refresh,
/// which would mean re-pulling every warehouse from the ERP on each stock taking.
/// DI registration is in CatalogModule.AddCatalogModule().
/// </summary>
internal sealed class CatalogManufactureStockSyncAdapter : IManufactureCatalogStockSync
{
    private readonly CatalogCacheStore _cacheStore;
    private readonly ILotsClient _lotsClient;
    private readonly ILogger<CatalogManufactureStockSyncAdapter> _logger;

    public CatalogManufactureStockSyncAdapter(
        CatalogCacheStore cacheStore,
        ILotsClient lotsClient,
        ILogger<CatalogManufactureStockSyncAdapter> logger)
    {
        _cacheStore = cacheStore ?? throw new ArgumentNullException(nameof(cacheStore));
        _lotsClient = lotsClient ?? throw new ArgumentNullException(nameof(lotsClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task SyncErpStockTakingAsync(
        string productCode,
        decimal newStockAmount,
        CancellationToken cancellationToken = default)
    {
        var lots = await LoadLotsAsync(productCode, cancellationToken);
        _cacheStore.ApplyErpStockTaking(productCode, newStockAmount, lots);
    }

    /// <summary>
    /// Reloads the product's lots from the ERP. Returns null when the ERP call fails, so that
    /// the stock level is still refreshed and the previously known lots are kept rather than
    /// being wiped by an empty result.
    /// </summary>
    private async Task<IReadOnlyList<CatalogLot>?> LoadLotsAsync(string productCode, CancellationToken cancellationToken)
    {
        try
        {
            return await _lotsClient.GetAsync(productCode, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to reload lots for {ProductCode} after stock taking. Stock level is still refreshed, " +
                "but lot detail stays stale until the next background refresh",
                productCode);
            return null;
        }
    }
}
