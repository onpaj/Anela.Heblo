## Module
Analytics

## Finding

`backend/src/Anela.Heblo.Application/Features/Analytics/Infrastructure/AnalyticsRepository.cs`, line 120, inside `StreamProductsWithSalesAsync`:

```csharp
for (int i = 0; i < allProducts.Count; i += batchSize)
{
    var batch = allProducts.Skip(i).Take(batchSize);

    foreach (var product in batch)
    {
        // ... yield return new AnalyticsProduct { ... }
    }

    // Allow garbage collection between batches
    GC.Collect();   // ← line 120
}
```

The comment states the intent is to "allow garbage collection between batches," but calling `GC.Collect()` does not allow the GC to run — it forces a **blocking, full-generation collection** synchronously. The .NET GC is generational and runs automatically; an explicit `GC.Collect()` in a hot loop forces a Gen2 collection regardless of memory pressure, pausing all threads for the duration on every batch iteration.

The overall approach is also misleading: `allProducts` is already fully materialized as a `List<CatalogAggregate>` before the loop begins (line 39: `var allProducts = await _catalogRepository.GetProductsWithSalesInPeriod(...)`), so peak memory is already allocated. The batching reduces neither peak memory nor allocation; it only adds unnecessary overhead and these forced GC pauses.

## Why it matters

Every margin report request (5 use cases fan out through this method) suffers unnecessary GC pause overhead on each 100-product batch boundary. For a catalog with 500+ products the loop triggers 5+ forced Gen2 collections per request. This is a performance antipattern documented by Microsoft ("Do not call GC.Collect except in a few well-defined scenarios — it can actually cause more harm than good in production code").

## Suggested fix

Remove the `GC.Collect()` call unconditionally — the runtime GC handles collection automatically and more efficiently. The line has no beneficial effect:

```csharp
// Before
    GC.Collect();   // remove this line

// After
    // (nothing)
```

The broader memory issue (fully materialising all products before streaming) is a separate concern addressed in the cross-module boundary issue — once `IAnalyticsProductSource` is owned by Analytics, the adapter can yield products directly from the EF query rather than loading all at once.

---
_Filed by daily arch-review routine on 2026-05-26._