## Module
DataQuality

## Finding
`ProductPairingDqtComparer` (`backend/src/Anela.Heblo.Application/Features/DataQuality/Services/ProductPairingDqtComparer.cs`, lines 1 and 13/19–21) directly imports and injects `ICatalogResilienceService` from the **Catalog** module's Application layer:

```csharp
using Anela.Heblo.Application.Features.Catalog.Infrastructure; // line 1

private readonly ICatalogResilienceService _resilienceService; // line 13

public ProductPairingDqtComparer(
    IEshopStockClient eshopStockClient,
    IErpStockClient erpStockClient,
    ICatalogResilienceService resilienceService, // line 21
    ILogger logger)
```

`ICatalogResilienceService` is defined in `Anela.Heblo.Application.Features.Catalog.Infrastructure.CatalogResilienceService` — it is a Catalog-module-internal Application-layer service with a Polly circuit-breaker pipeline tuned for Catalog's external HTTP calls. DataQuality's Application layer is directly coupling to Catalog's Application-layer internals.

This is the same boundary violation pattern as #3433 (`FinancialOverview: StockValueService directly imports Catalog-owned ERP interfaces`) — the Catalog module is the repeated source of the leak.

## Why it matters
Per `development_guidelines.md`:
> **Direct access to another module's entities** — Violates boundaries, tight coupling

And the cross-module adapter rule:
> **Consumer (A) defines the contract.** Module A declares an interface in its own `Contracts/` folder.

A DataQuality comparer should not import infrastructure internals from Catalog. If Catalog's resilience policy changes (timeout, retry count, circuit-breaker threshold), DataQuality's behavior changes without any change to DataQuality code — invisible coupling. It also prevents DataQuality from configuring resilience appropriate to its own workload.

## Suggested fix
Two options, both consistent with the documented pattern:

**Option A — DataQuality owns a resilience contract:**
Define `IDqtResilienceService` in `DataQuality/Contracts/` (mirroring `ICatalogResilienceService`'s interface shape), move the implementation to Catalog's infrastructure as `CatalogDqtResilienceAdapter`, and register in `CatalogModule.cs`. DataQuality then depends only on its own contract.

**Option B — Push resilience into the adapter:**
Move the `_resilienceService.ExecuteWithResilienceAsync(...)` calls from `ProductPairingDqtComparer` into the Catalog-side adapters that implement `IEshopStockClient` and `IErpStockClient`. DataQuality calls the interface; the adapter handles resilience internally. `ProductPairingDqtComparer` drops the `ICatalogResilienceService` dependency entirely.

Option B is simpler and removes the dependency completely.

---
_Filed by daily arch-review routine on 2026-07-01._
