using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

/// <summary>
/// Refreshes internal reference data (stock taking, manufacture difficulty settings) and
/// performs the manufacture-cost cross-reference pass over the cached catalog aggregate.
/// </summary>
public sealed class CatalogReferenceRefreshService
{
    private readonly IStockTakingRepository _stockTakingRepository;
    private readonly IManufactureDifficultyRepository _manufactureDifficultyRepository;
    private readonly TimeProvider _timeProvider;
    private readonly CatalogCacheStore _cacheStore;
    private readonly ILogger<CatalogReferenceRefreshService> _logger;

    public CatalogReferenceRefreshService(
        IStockTakingRepository stockTakingRepository,
        IManufactureDifficultyRepository manufactureDifficultyRepository,
        TimeProvider timeProvider,
        CatalogCacheStore cacheStore,
        ILogger<CatalogReferenceRefreshService> logger)
    {
        _stockTakingRepository = stockTakingRepository ?? throw new ArgumentNullException(nameof(stockTakingRepository));
        _manufactureDifficultyRepository = manufactureDifficultyRepository ?? throw new ArgumentNullException(nameof(manufactureDifficultyRepository));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _cacheStore = cacheStore ?? throw new ArgumentNullException(nameof(cacheStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task RefreshStockTakingData(CancellationToken ct)
    {
        _cacheStore.SetStockTakingData((await _stockTakingRepository.GetAllAsync(ct)).ToList());
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
