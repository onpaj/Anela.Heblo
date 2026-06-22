## Module
ShoptetOrders

## Finding
`ShoptetApiPackingOrderClient` (`backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiPackingOrderClient.cs`) injects and directly calls repository interfaces owned by two other feature modules:

```csharp
// Lines 19-21
private readonly ICatalogRepository _catalog;       // Catalog module domain interface
private readonly ICarrierCoolingRepository _carrierCooling; // Logistics module domain interface
```

It then reads Catalog domain entity properties directly (lines 81–88):
```csharp
var coolingByCode = catalogItems.ToDictionary(kv => kv.Key, kv => kv.Value.Properties.Cooling);
var weightByCode  = catalogItems.ToDictionary(
    kv => kv.Key,
    kv => kv.Value.GrossWeight.HasValue ? (int?)((int)kv.Value.GrossWeight.Value)
        : kv.Value.NetWeight.HasValue ? (int)kv.Value.NetWeight.Value
        : (int?)null);
// ...
ImageUrl = catalogItems.TryGetValue(i.ProductCode, out var c) ? c.Image : null,
```

`ICatalogRepository` and `ICarrierCoolingRepository` are broad repository contracts owned by their respective modules; they are not narrow, consumer-owned contracts defined to serve ShoptetOrders specifically.

## Why it matters
The project's documented cross-module communication pattern (described for `ILeafletKnowledgeSource` in `docs/architecture/development_guidelines.md`) requires the consuming module to **own the contract**: a narrow interface is defined in the consumer's `Contracts/` folder, the provider implements an adapter, and the provider registers the binding. This inverts the dependency so the consumer never knows about the provider's entity structure.

The current adapter breaks this in two ways:
1. It references broad repository interfaces from two foreign modules (coupling to Catalog and Logistics).
2. It accesses Catalog entity internals (`.Properties.Cooling`, `.GrossWeight`, `.NetWeight`, `.Image`) — so a Catalog entity-shape change silently breaks packing order loading.

Business enrichment logic (which catalog properties matter for packing, how weight falls back, how cooling is computed) belongs in the Application layer, not in an adapter that is also responsible for HTTP translation.

## Suggested fix
Define a narrow consumer-owned contract in ShoptetOrders' own `Contracts/` folder:

```csharp
// backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Contracts/IPackingProductSource.cs
public interface IPackingProductSource
{
    Task<IReadOnlyDictionary<string, PackingProductInfo>> GetByCodesAsync(
        IEnumerable<string> productCodes, CancellationToken ct = default);
}

public class PackingProductInfo
{
    public Cooling Cooling { get; init; }
    public int? WeightGrams { get; init; }
    public string? ImageUrl { get; init; }
}
```

Have the Catalog module implement `IPackingProductSource` as an adapter (analogous to `KnowledgeBaseLeafletSourceAdapter`), registered in `CatalogModule`. Do the same for carrier-cooling if a narrow contract is warranted.

`ShoptetApiPackingOrderClient` then injects `IPackingProductSource` instead of `ICatalogRepository`, removing all knowledge of Catalog entity internals from the adapter.

---
_Filed by daily arch-review routine on 2026-06-21._
