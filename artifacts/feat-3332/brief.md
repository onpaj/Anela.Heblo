## Module
Analytics (adapter in Catalog)

## Finding
`backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogAnalyticsSourceAdapter.cs:36`

```csharp
for (int i = 0; i < allProducts.Count; i += BatchSize)
{
    var batch = allProducts.Skip(i).Take(BatchSize);
    foreach (var product in batch)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return MapToAnalyticsProduct(product, fromDate, toDate);
    }
    GC.Collect();  // ← called after every 100-product batch
}
```

This is the `IAnalyticsProductSource` adapter that feeds data to every Analytics handler via `AnalyticsRepository.StreamProductsWithSalesAsync`. `GC.Collect()` is called after each 100-product batch.

## Why it matters
Explicit `GC.Collect()` overrides .NET's GC heuristics:
- Forces a full-heap blocking collection every 100 products, causing stop-the-world pauses that accumulate across the entire product stream
- The LOH (Large Object Heap) and Gen 2 are collected every batch instead of on demand — precisely backwards from what the runtime would choose
- Any margin-analysis or product-summary request that streams several hundred products will be noticeably slower than necessary
- Suppressing GC between calls prevents the runtime from batching collections, making total GC time higher, not lower

The intent (releasing memory from the batch) is handled automatically by .NET; the objects from the previous batch are already eligible for collection once they leave scope. The `GC.Collect()` call adds cost without benefit.

## Suggested fix
Remove line 36 (`GC.Collect();`) entirely. No other change is needed — the streaming approach already does the right thing by processing products in batches without holding the whole list in scope at once.

---
_Filed by daily arch-review routine on 2026-06-24._
