## Module
KnowledgeBase

## Finding
`ProductEnrichmentCache` in the KnowledgeBase module directly resolves and calls `ICatalogRepository` — a domain-layer interface owned by the Catalog module:

```csharp
// backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Pipeline/ProductEnrichmentCache.cs:1
using Anela.Heblo.Domain.Features.Catalog;

// line 40
var repository = scope.ServiceProvider.GetRequiredService<ICatalogRepository>();
var products = await repository.FindAsync(
    p => p.Type == ProductType.Product || p.Type == ProductType.Goods, ct);
```

It then reads `p.ProductCode`, `p.ProductName`, and `p.Url` from `CatalogAggregate` domain entities.

## Why it matters
`development_guidelines.md` states: _"Communication between modules exclusively through `contracts/`"_ and _"No direct access to another module's entities."_ This coupling means:
- KnowledgeBase has a compile-time (and runtime) dependency on Catalog's domain and persistence layers.
- Any change to `CatalogAggregate` or `ICatalogRepository` can silently break the KnowledgeBase pipeline.
- It becomes impossible to test `ProductEnrichmentCache` in isolation from the Catalog module.

## Suggested fix
Define a narrow read-only contract in the Catalog module:

```csharp
// Catalog/Contracts/IProductCatalogQueryService.cs
public interface IProductCatalogQueryService
{
    Task<IReadOnlyList<ProductCatalogEntry>> GetActiveProductsAsync(CancellationToken ct = default);
}

public class ProductCatalogEntry
{
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string? Url { get; set; }
}
```

Implement it inside the Catalog module (wrapping `ICatalogRepository`), register it in `CatalogModule.cs`, then inject `IProductCatalogQueryService` into `ProductEnrichmentCache` instead of `ICatalogRepository`. This removes the cross-module entity access while keeping the cache logic identical.

---
_Filed by daily arch-review routine on 2026-05-13._