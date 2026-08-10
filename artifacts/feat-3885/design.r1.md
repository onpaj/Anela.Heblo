# Design: Fix cache isolation violations in CatalogDataRefreshService

## Component Design

No new components. All changes are confined to `CatalogDataRefreshService` (`backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogDataRefreshService.cs`), consuming the existing `CatalogCacheStore` API unchanged.

### `RefreshManufactureDifficultySettingsData(string? product, CancellationToken ct)` — single-product branch

Responsibility: given a product code and its freshly-loaded `ManufactureDifficultySetting` list, update the cached per-product dictionary and the merged snapshot (if one exists) **without mutating any object a concurrent reader might already hold a reference to**.

```
if (product == null)
{
    // unchanged: full-dictionary rebuild via SetManufactureDifficultySettingsData
}
else
{
    // 1. Copy-then-set the dictionary
    var existingDict = _cacheStore.GetManufactureDifficultySettingsData();
    var newDict = new Dictionary<string, List<ManufactureDifficultySetting>>(existingDict)
    {
        [product] = difficultySettings.ToList()
    };
    _cacheStore.SetManufactureDifficultySettingsData(newDict);

    // 2. Copy-then-swap the live snapshot, if any
    var current = _cacheStore.TryGetCurrent();
    if (current != null)
    {
        var productAggregate = current.SingleOrDefault(s => s.ProductCode == product);
        if (productAggregate != null)
        {
            var updated = current.Select(p =>
            {
                if (p != productAggregate) return p;
                var clone = p.Clone();
                clone.ManufactureDifficultySettings.Assign(difficultySettings, _timeProvider.GetUtcNow().UtcDateTime);
                return clone;
            }).ToList();

            await _cacheStore.ReplaceCacheAtomicallyAsync(updated);
        }
    }
}
```

Contract: on return, every `CatalogAggregate` and `Dictionary` reference obtained from `_cacheStore` *before* this call remains exactly as it was before the call. Only newly-obtained references reflect the update.

### `RefreshManufactureCostData(CancellationToken ct)`

Responsibility: attach `ManufactureHistory` to every product that has manufacture-history entries, without mutating the live catalog list in place.

```
var manufactureMap = _cacheStore.GetManufactureHistoryData()
    .GroupBy(p => p.ProductCode)
    .ToDictionary(k => k.Key, v => v.ToList());

var catalogData = _cacheStore.GetCatalogData();
var updated = (catalogData ?? []).Select(product =>
{
    if (!manufactureMap.TryGetValue(product.ProductCode, out var manufactures)) return product;
    var clone = product.Clone();
    clone.ManufactureHistory = manufactures.ToList();
    return clone;
}).ToList();

await _cacheStore.ReplaceCacheAtomicallyAsync(updated);
```

Same contract as above: pre-call references are untouched; the method installs a new list via the atomic swap instead of writing through old references.

## Data Schemas

No schema, DTO, or persisted-data changes. Types involved are all pre-existing:
- `Dictionary<string, List<ManufactureDifficultySetting>>` (per-source cache value for `CachedManufactureDifficultySettingsDataKey`).
- `List<CatalogAggregate>` (merged snapshot, `CurrentCatalogCacheKey`/`StaleCatalogCacheKey`).
- `CatalogAggregate.Clone()` / `ManufactureDifficultyConfiguration.Clone()` / `.Assign(...)` — existing methods, reused verbatim, no signature changes.

No API request/response shapes are touched — `ICatalogRepository.RefreshManufactureDifficultySettingsData` keeps its existing `Task RefreshManufactureDifficultySettingsData(string?, CancellationToken)` signature, called the same way from `CreateManufactureDifficultyHandler`, `UpdateManufactureDifficultyHandler`, and `DeleteManufactureDifficultyHandler`.
