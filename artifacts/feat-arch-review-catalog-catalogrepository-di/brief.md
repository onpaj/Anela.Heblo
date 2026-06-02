## Module
Catalog

## Finding
`CatalogRepository` (`Application/Features/Catalog/CatalogRepository.cs`) imports and directly injects interfaces owned by three other feature modules:

| Constructor parameter | Interface | Owning module |
|---|---|---|
| `ITransportBoxRepository` (line 38) | `Domain.Features.Logistics.Transport` | **Logistics** |
| `IPurchaseOrderRepository` (line 43) | `Domain.Features.Purchase` | **Purchase** |
| `IManufactureOrderRepository` (line 44) | `Domain.Features.Manufacture` | **Manufacture** |
| `IManufactureHistoryClient` (line 45) | `Domain.Features.Manufacture` | **Manufacture** |
| `IManufacturedProductInventoryRepository` (line 46) | `Domain.Features.Manufacture.Inventory` | **Manufacture** |
| `IManufactureClient` (line 40) | `Domain.Features.Manufacture` | **Manufacture** (also unused — see below) |

`IManufactureClient` is assigned in the constructor (line 103) but **never called anywhere** in the 962-line file — a dead cross-module dependency.

The using directives at lines 17–19 make the module coupling explicit:
```csharp
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Manufacture.Inventory;
using Anela.Heblo.Domain.Features.Purchase;
```

## Why it matters
The guidelines (`development_guidelines.md` §Forbidden Practices) explicitly forbid "direct access to another module's entities" and require communication only through consumer-owned contract interfaces. The documented pattern (`ILeafletKnowledgeSource` example) mandates:
1. Consumer (Catalog) defines the contract in its own `Contracts/` folder.
2. Provider (Logistics/Purchase/Manufacture) implements an adapter.

Violating this means:
- Any internal change to Logistics, Purchase, or Manufacture repositories breaks Catalog compilation.
- The full surface area of each provider module is exposed to Catalog (far more than needed).
- The stated goal of per-module independent deployability is unreachable without rework.
- This is the symmetric problem to #1960 (Logistics consuming Catalog-owned interfaces directly).

## Suggested fix
1. Declare Catalog-owned source interfaces in `Application/Features/Catalog/Contracts/`, e.g.:
   - `ICatalogTransportSource` — exposes only `GetProductsInTransportAsync`, `GetProductsInReserveAsync`, `GetProductsInQuarantineAsync`
   - `ICatalogPurchaseSource` — exposes only `GetOrderedQuantitiesAsync`
   - `ICatalogManufactureSource` — exposes only `GetPlannedQuantitiesAsync`, `GetManufactureHistoryAsync`, `GetManufacturedInventoryAsync`
2. In each provider module's `Infrastructure/`, implement an adapter that delegates to the existing repository.
3. Register the bindings in each provider's module registration file.
4. Remove `IManufactureClient` entirely (it is already unused).

---
_Filed by daily arch-review routine on 2026-05-29._