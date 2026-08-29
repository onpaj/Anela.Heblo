# Design: DataQuality-Catalog Module Boundary Decoupling for ProductPairingDqtComparer

## Component Design

### `IDqtEshopStockSource` (DataQuality-owned contract)
- **Location:** `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IDqtEshopStockSource.cs`
- **Responsibility:** Sole abstraction `ProductPairingDqtComparer` uses to obtain eshop-side stock snapshots. Knows nothing about Catalog, Shoptet, or HTTP.
- **Contract:**
  ```csharp
  public interface IDqtEshopStockSource
  {
      Task<IReadOnlyList<DqtEshopStockItem>> ListAsync(CancellationToken cancellationToken);
  }
  ```
- **Consumers:** `ProductPairingDqtComparer` (constructor-injected).
- **Implementors:** `DataQualityEshopStockSourceAdapter` (Catalog module).

### `IDqtErpStockSource` (DataQuality-owned contract)
- **Location:** `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IDqtErpStockSource.cs`
- **Responsibility:** Sole abstraction `ProductPairingDqtComparer` uses to obtain ERP-side stock snapshots, already reduced to the DataQuality-relevant `IsSellable` boolean (no `ProductType` enum crosses the boundary).
- **Contract:**
  ```csharp
  public interface IDqtErpStockSource
  {
      Task<IReadOnlyList<DqtErpStockItem>> ListAsync(CancellationToken cancellationToken);
  }
  ```
- **Consumers:** `ProductPairingDqtComparer`.
- **Implementors:** `DataQualityErpStockSourceAdapter` (Catalog module).

### `DataQualityEshopStockSourceAdapter` (Catalog-owned adapter)
- **Location:** `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/DataQualityEshopStockSourceAdapter.cs`
- **Modifier:** `internal sealed class`
- **Responsibility:** Implements `IDqtEshopStockSource` by delegating to `IEshopStockClient` and projecting each `EshopStock` onto a `DqtEshopStockItem` (`Code`, `PairCode`, `Name` only — pure field mapping, no business logic, no caching).
- **Dependency:** `IEshopStockClient _inner` (constructor-injected, Catalog-internal type — never exposed outside this class for this use case).
- **Return-type normalization:** `IEshopStockClient.ListAsync` returns `List<EshopStock>`; the adapter converts to `IReadOnlyList<DqtEshopStockItem>` so the DataQuality-facing contract does not inherit that mutability asymmetry.

### `DataQualityErpStockSourceAdapter` (Catalog-owned adapter)
- **Location:** `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/DataQualityErpStockSourceAdapter.cs`
- **Modifier:** `internal sealed class`
- **Responsibility:** Implements `IDqtErpStockSource` by delegating to `IErpStockClient`, projecting each `ErpStock` onto a `DqtErpStockItem`, and computing `IsSellable` from the Catalog-owned `ProductType` enum (`ProductTypeId == (int)ProductType.Goods || ProductTypeId == (int)ProductType.Product`). This is the only place this comparison logic lives after the refactor — it is deleted from `ProductPairingDqtComparer`.
- **Dependency:** `IErpStockClient _inner`.

### `ProductPairingDqtComparer` (DataQuality module, modified in place)
- **Location:** `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/ProductPairingDqtComparer.cs`
- **Responsibility (unchanged):** Implements `IDriftDqtComparer.CompareAsync(from, to, ct)` — cross-checks Shoptet product pairing (`PairCode`/`Code` resolution) against sellable ERP products, producing `DriftMismatch` records and a `TotalChecked` count.
- **Change in dependencies:** constructor now takes `IDqtEshopStockSource eshopStockSource` and `IDqtErpStockSource erpStockSource` in place of `IEshopStockClient`/`IErpStockClient`. No `using Anela.Heblo.Domain.Features.Catalog*` remains.
- **Change in internals:** local variables become `IReadOnlyList<DqtEshopStockItem>` / `IReadOnlyList<DqtErpStockItem>`; the private `static bool IsSellable(ErpStock)` helper is removed — call sites use `p.IsSellable` directly.
- **Unchanged:** both `_resilienceService.ExecuteWithResilienceAsync(...)` wrapper call sites (operation names `"ProductPairingDqtComparer.EshopList"` / `"ProductPairingDqtComparer.ErpList"`), their `try`/`catch`/`LogWarning` structure, and all pairing/mismatch/`TotalChecked` logic.

### DI wiring (`CatalogModule.AddCatalogModule`)
- **Location:** `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs`, appended to the existing `// DataQuality owns the query contracts; Catalog (this module) provides the adapter implementations.` block (currently registering `IStockOperationQuery`, `IStockTakingQuery`, `IMaterialLotStockQuery`, `IDqtResilienceService`).
- **Registrations added:**
  ```csharp
  services.AddScoped<IDqtEshopStockSource, DataQualityEshopStockSourceAdapter>();
  services.AddScoped<IDqtErpStockSource, DataQualityErpStockSourceAdapter>();
  ```
- **Lifetime rationale:** `Scoped` is safe regardless of the wrapped client's lifetime — `IEshopStockClient` is a typed `HttpClient` (transient-by-default via `AddHttpClient`) and `IErpStockClient` is `AddSingleton`; a `Scoped` consumer of either is not a captive-dependency violation.
- **Binding ownership:** registered in `CatalogModule`, not `DataQualityModule` — the provider module owns the adapter binding, matching the sibling `IStockOperationQuery`/etc. pattern.

### Component interaction (call flow, unchanged shape)

```
DriftDqtJobRunner
  └─ ProductPairingDqtComparer.CompareAsync(from, to, ct)
       ├─ IDqtResilienceService.ExecuteWithResilienceAsync(
       │     () => IDqtEshopStockSource.ListAsync(ct), "ProductPairingDqtComparer.EshopList")
       │        └─ DataQualityEshopStockSourceAdapter.ListAsync(ct)   [Catalog]
       │             └─ IEshopStockClient.ListAsync(ct)               [Catalog, unchanged]
       └─ IDqtResilienceService.ExecuteWithResilienceAsync(
             () => IDqtErpStockSource.ListAsync(ct), "ProductPairingDqtComparer.ErpList")
                └─ DataQualityErpStockSourceAdapter.ListAsync(ct)     [Catalog]
                     └─ IErpStockClient.ListAsync(ct)                 [Catalog, unchanged]
```

### Boundary test update
`ModuleBoundariesTests.cs` — the four `DataQualityCatalogAllowlist` entries for `ProductPairingDqtComparer` (lines ~134–143) are removed; the preceding comment block is updated to the "Empty — ..." style already used for `LeafletAllowlist`/`ArticleAllowlist`, documenting that this DataQuality→Catalog violation is resolved. No production behavior; this is what makes the decoupling durable against regression.

## Data Schemas

All types below are in-memory only — no database schema, migration, or persisted-storage change. No MediatR request/response or HTTP contract is touched.

### DataQuality-owned contracts (`Application/Features/DataQuality/Contracts/`)

| Type | Kind | Namespace | Members |
|---|---|---|---|
| `IDqtEshopStockSource` | interface | `Anela.Heblo.Application.Features.DataQuality.Contracts` | `Task<IReadOnlyList<DqtEshopStockItem>> ListAsync(CancellationToken cancellationToken)` |
| `IDqtErpStockSource` | interface | `Anela.Heblo.Application.Features.DataQuality.Contracts` | `Task<IReadOnlyList<DqtErpStockItem>> ListAsync(CancellationToken cancellationToken)` |
| `DqtEshopStockItem` | class (not record) | `Anela.Heblo.Application.Features.DataQuality.Contracts` | `Code: string`, `PairCode: string`, `Name: string` — all `{ get; set; }` |
| `DqtErpStockItem` | class (not record) | `Anela.Heblo.Application.Features.DataQuality.Contracts` | `ProductCode: string`, `ProductName: string`, `IsSellable: bool` — all `{ get; set; }` |

Field-mapping origin (source → snapshot), for traceability:

| `DqtEshopStockItem` field | Sourced from (`EshopStock`) |
|---|---|
| `Code` | `EshopStock.Code` |
| `PairCode` | `EshopStock.PairCode` |
| `Name` | `EshopStock.Name` |

| `DqtErpStockItem` field | Sourced from (`ErpStock`) |
|---|---|
| `ProductCode` | `ErpStock.ProductCode` |
| `ProductName` | `ErpStock.ProductName` |
| `IsSellable` | `ErpStock.ProductTypeId == (int)ProductType.Goods \|\| ErpStock.ProductTypeId == (int)ProductType.Product` (computed in the Catalog-side adapter — the raw `ProductTypeId`/`ProductType` enum never crosses into DataQuality) |

Neither DTO nor interface carries any field beyond what `ProductPairingDqtComparer` reads today — no speculative members.

### Catalog-owned adapters (`Application/Features/Catalog/Infrastructure/`)

| Type | Kind | Implements | Wraps | Namespace |
|---|---|---|---|---|
| `DataQualityEshopStockSourceAdapter` | `internal sealed class` | `IDqtEshopStockSource` | `IEshopStockClient` | `Anela.Heblo.Application.Features.Catalog.Infrastructure` |
| `DataQualityErpStockSourceAdapter` | `internal sealed class` | `IDqtErpStockSource` | `IErpStockClient` | `Anela.Heblo.Application.Features.Catalog.Infrastructure` |

Both adapters are the only code outside `Anela.Heblo.Domain.Features.Catalog*` that references `EshopStock`, `ErpStock`, or `ProductType` for this use case.

### Method signature diff on `ProductPairingDqtComparer`

| Aspect | Before | After |
|---|---|---|
| Constructor params | `IEshopStockClient eshopStockClient, IErpStockClient erpStockClient` | `IDqtEshopStockSource eshopStockSource, IDqtErpStockSource erpStockSource` |
| Eshop locals | `List<EshopStock>` | `IReadOnlyList<DqtEshopStockItem>` |
| Erp locals | `IReadOnlyList<ErpStock>` | `IReadOnlyList<DqtErpStockItem>` |
| Sellability filter | `erpProducts.Where(IsSellable)` + private `static bool IsSellable(ErpStock)` | `erpProducts.Where(p => p.IsSellable)` (helper removed) |
| Resilience call targets | `_eshopStockClient.ListAsync(ct)` / `_erpStockClient.ListAsync(ct)` | `_eshopStockSource.ListAsync(ct)` / `_erpStockSource.ListAsync(ct)` |

Everything else — `DriftMismatch` shape, `ProductPairingMismatch` flag combinations, `Details` message text, `TotalChecked` formula, resilience operation-name strings (`"ProductPairingDqtComparer.EshopList"`, `"ProductPairingDqtComparer.ErpList"`) — is unchanged; this is a pure type-substitution refactor.

### DI registration shape (`CatalogModule.cs`)

```csharp
// DataQuality owns the query contracts; Catalog (this module) provides the adapter implementations.
services.AddScoped<IStockOperationQuery, DataQualityStockOperationQueryAdapter>();
services.AddScoped<IStockTakingQuery, DataQualityStockTakingQueryAdapter>();
services.AddScoped<IMaterialLotStockQuery, DataQualityMaterialLotStockQueryAdapter>();
services.AddScoped<IDqtResilienceService, DataQualityResilienceAdapter>();
services.AddScoped<IDqtEshopStockSource, DataQualityEshopStockSourceAdapter>();   // new
services.AddScoped<IDqtErpStockSource, DataQualityErpStockSourceAdapter>();      // new
```

### Test-double shape (`ProductPairingDqtComparerTests.cs`)

| Before (mocked type) | After (mocked type) |
|---|---|
| `Mock<IEshopStockClient>` returning `List<EshopStock>` | `Mock<IDqtEshopStockSource>` returning `IReadOnlyList<DqtEshopStockItem>` |
| `Mock<IErpStockClient>` returning `IReadOnlyList<ErpStock>` | `Mock<IDqtErpStockSource>` returning `IReadOnlyList<DqtErpStockItem>` |
| `new ErpStock { ProductTypeId = 1 /* Goods */ }` | `new DqtErpStockItem { IsSellable = true /* Goods */ }` |

Test scenarios, inputs, and expected `Mismatches`/`TotalChecked` assertions are unchanged in intent — only the mocked interface and DTO types shift.
