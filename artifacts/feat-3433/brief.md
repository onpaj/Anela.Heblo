## Module
FinancialOverview

## Finding
`backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/StockValueService.cs` (lines 1–4) directly imports and injects two interfaces owned by the **Catalog** module:

```csharp
using Anela.Heblo.Domain.Features.Catalog.Price;   // IProductPriceErpClient
using Anela.Heblo.Domain.Features.Catalog.Stock;   // IErpStockClient
```

`IErpStockClient` is defined in `Domain.Features.Catalog.Stock` (a Catalog-module domain type with a 22-member Stock domain folder) and `IProductPriceErpClient` is in `Domain.Features.Catalog.Price`. Both are Catalog-module-owned contracts. The FinancialOverview module's Application-layer service should not directly reference another module's domain types.

The domain abstraction `IStockValueService` (`Domain.Features.FinancialOverview.IStockValueService`) exists and is the correct boundary — but the implementation (`StockValueService`) lives inside FinancialOverview's own `Services/` folder instead of in the Catalog module.

## Why it matters
This violates the cross-module communication rule from `development_guidelines.md`:

> **Direct access to another module's entities** — Violates boundaries, tight coupling

And the adapter pattern documented under *Cross-Module Communication*:

> **Provider (B) implements the contract via an adapter.** The adapter lives in module B's `Infrastructure/`.

With the current placement, any change to `IErpStockClient` or `IProductPriceErpClient` (Catalog concerns) requires editing FinancialOverview code. If Catalog ever moves to its own DbContext/service boundary, FinancialOverview breaks silently.

## Suggested fix
Move `StockValueService` to the **Catalog** module as an adapter:

```
Application/Features/Catalog/Infrastructure/FinancialOverviewStockValueAdapter.cs
```

Register the binding in `CatalogModule.cs`:

```csharp
services.AddScoped<IStockValueService, FinancialOverviewStockValueAdapter>();
```

`FinancialOverview` continues to own `IStockValueService` (Domain layer) and knows nothing about `IErpStockClient` or `IProductPriceErpClient`. The Catalog module implements the adapter and wires it. This matches the existing `ILeafletKnowledgeSource` / `KnowledgeBaseLeafletSourceAdapter` pattern documented in `development_guidelines.md`.

---
_Filed by daily arch-review routine on 2026-06-30._
