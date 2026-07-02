# Design: Remove DataQuality → Catalog Application-layer boundary violation in ProductPairingDqtComparer

## Component Design

This is a pure dependency-direction refactor (Option A: consumer-owned contract + provider-owned adapter), structurally identical to the two existing precedents in this module pair (`IStockOperationQuery`/`DataQualityStockOperationQueryAdapter` and `IStockTakingQuery`/`DataQualityStockTakingQueryAdapter`). No new runtime behavior is introduced anywhere in this design — every component below is either a pass-through interface, a one-line delegating adapter, or a type substitution at an existing call site.

```
DataQuality module (consumer)                    Catalog module (provider)
──────────────────────────────                   ──────────────────────────
ProductPairingDqtComparer                          CatalogModule.AddCatalogModule()
  ├─ IEshopStockClient        (unchanged,             registers:
  │   Domain.Catalog.Stock,                           IDqtResilienceService →
  │   out of scope)                                     DataQualityResilienceAdapter (Scoped)
  ├─ IErpStockClient          (unchanged,
  │   Domain.Catalog.Stock,                          DataQualityResilienceAdapter
  │   out of scope)                                    (Catalog.Infrastructure, internal sealed)
  └─ IDqtResilienceService  ──────depends on──────►      └─ ICatalogResilienceService (injected)
      (DataQuality.Contracts,                               └─ CatalogResilienceService (Singleton,
       NEW)                                                      Polly pipeline, unchanged)
```

### `IDqtResilienceService` (new, DataQuality-owned)
- **Location:** `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IDqtResilienceService.cs`
- **Namespace:** `Anela.Heblo.Application.Features.DataQuality.Contracts`
- **Responsibility:** Defines the resilience-execution contract that `ProductPairingDqtComparer` depends on, owned by the consumer module (DataQuality) per the project's cross-module boundary rule. Shape is byte-for-byte identical to `ICatalogResilienceService.ExecuteWithResilienceAsync<T>`, so no call-site logic changes are required.
- **Visibility:** `public` (consumed across module boundary by Catalog's adapter implementation).

### `DataQualityResilienceAdapter` (new, Catalog-owned)
- **Location:** `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/DataQualityResilienceAdapter.cs`
- **Namespace:** `Anela.Heblo.Application.Features.Catalog.Infrastructure`
- **Responsibility:** Implements `IDqtResilienceService` by delegating 1:1 to the existing `ICatalogResilienceService` singleton (`CatalogResilienceService`). Introduces zero new logic — pure pass-through of `operation`, `operationName`, and `cancellationToken`, and the return value.
- **Visibility:** `internal sealed`, matching the two sibling adapters (`DataQualityStockOperationQueryAdapter`, `DataQualityStockTakingQueryAdapter`) in the same folder.
- **Dependency:** Constructor-injects `ICatalogResilienceService`.

### `ProductPairingDqtComparer` (modified, DataQuality-owned)
- **Location:** `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/ProductPairingDqtComparer.cs`
- **Change:** Constructor parameter/field type changes from `ICatalogResilienceService` to `IDqtResilienceService`; `using Anela.Heblo.Application.Features.Catalog.Infrastructure;` is removed and replaced with `using Anela.Heblo.Application.Features.DataQuality.Contracts;`. `IEshopStockClient`/`IErpStockClient` dependencies (Domain-layer, out of scope) are unchanged. `CompareAsync` method body, mismatch-detection logic, and exception propagation are unchanged.

### DI Registration (Catalog-owned, `CatalogModule.cs`)
- Registered in `CatalogModule.AddCatalogModule()`, grouped with the existing `IStockOperationQuery`/`IStockTakingQuery` DataQuality-facing adapter registrations, with an explanatory comment matching the existing style.
- **Lifetime:** `Scoped`, matching sibling adapters — even though the underlying `ICatalogResilienceService` is `Singleton`. A scoped adapter depending on a singleton is safe (not a captive-dependency hazard); the adapter itself is stateless.
- `DataQualityModule.cs` is not modified — registration ownership stays with the provider (Catalog), per the documented "provider registers" rule.

### Test updates
- `ProductPairingDqtComparerTests.cs`: mock type swap from `Mock<ICatalogResilienceService>` to `Mock<IDqtResilienceService>`; all 5 existing test assertions unchanged.
- `ModuleBoundariesTests.cs`: remove the now-stale `ProductPairingDqtComparer -> ICatalogResilienceService` entry from `DataQualityCatalogAllowlist` (the architecture test enforcing the `"DataQuality -> Catalog"` boundary rule already exists in this file and will otherwise carry a dead allowlist entry that misdescribes the comparer's post-fix dependencies). The four remaining out-of-scope entries (`IEshopStockClient`, `IErpStockClient`, `ErpStock`, `ProductType`, `EshopStock`) are left untouched.
- Optional: adapter-level unit test for `DataQualityResilienceAdapter` verifying pure pass-through delegation (operation, operationName, cancellationToken, and return value all forwarded unchanged).

## Data Schemas

No data model, persistence, HTTP, or MediatR contract changes. This refactor is confined to the Application layer's internal DI graph and namespace boundaries.

**New contract:**
```csharp
namespace Anela.Heblo.Application.Features.DataQuality.Contracts;

public interface IDqtResilienceService
{
    Task<T> ExecuteWithResilienceAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken = default);
}
```

**New adapter:**
```csharp
namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

internal sealed class DataQualityResilienceAdapter : IDqtResilienceService
{
    private readonly ICatalogResilienceService _resilienceService;

    public DataQualityResilienceAdapter(ICatalogResilienceService resilienceService)
    {
        _resilienceService = resilienceService;
    }

    public Task<T> ExecuteWithResilienceAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken = default) =>
        _resilienceService.ExecuteWithResilienceAsync(operation, operationName, cancellationToken);
}
```

**DI registration** (in `CatalogModule.AddCatalogModule()`):
```csharp
// DataQuality owns the resilience contract; Catalog (this module) provides the adapter implementation.
services.AddScoped<IDqtResilienceService, DataQualityResilienceAdapter>();
```

**Modified consumer constructor:**
```csharp
ProductPairingDqtComparer(
    IEshopStockClient eshopStockClient,
    IErpStockClient erpStockClient,
    IDqtResilienceService resilienceService,   // was: ICatalogResilienceService
    ILogger<ProductPairingDqtComparer> logger)
```

`operationName` string values (`"ProductPairingDqtComparer.EshopList"`, `"ProductPairingDqtComparer.ErpList"`) pass through unchanged end-to-end, preserving Polly operation-key-based logging/circuit-breaker correlation. Retry count (3), circuit-breaker thresholds (50% failure ratio, min throughput 3, 1-minute sampling, 30s break), and 30s timeout in `CatalogResilienceService` are untouched.
