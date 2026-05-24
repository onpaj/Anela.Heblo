# Specification: Decouple Purchase Handlers from Catalog Domain via Consumer-Owned Contract

## Summary
Five Purchase handlers currently inject `ICatalogRepository` directly, violating module-boundary rules defined in `development_guidelines.md`. This work introduces a Purchase-owned read contract (`IMaterialCatalogService`) implemented by a Catalog-side adapter, replaces the direct injection in all five handlers, and extends the existing architectural test to prevent regression.

## Background
Per `docs/architecture/development_guidelines.md` (Cross-Module Communication Example, ADR-style guidance, and the working `ILeafletKnowledgeSource` pattern), modules must not depend on another module's domain interfaces. The consumer module owns a narrow contract; the provider supplies an adapter and registers the binding.

Today the Purchase module breaks this rule in five handlers:

- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/CreatePurchaseOrder/CreatePurchaseOrderHandler.cs:5,17,25`
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrder/UpdatePurchaseOrderHandler.cs:4,15,22`
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseOrderById/GetPurchaseOrderByIdHandler.cs:3,15,21`
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisHandler.cs:3,12,17`
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/RecalculatePurchasePrice/RecalculatePurchasePriceHandler.cs:2,11,16`

`ICatalogRepository` (in `Anela.Heblo.Domain.Features.Catalog`) is a fat interface (20+ methods: `RefreshTransportData`, `RefreshManufacturedData`, `RefreshReserveData`, … merge tracking, load-timestamp queries, analytics). Purchase actually uses only `GetByIdAsync`, `GetAllAsync`, and a subset of `CatalogAggregate` properties/methods.

Consequences of the current state:
- Purchase recompiles on any change to `ICatalogRepository`.
- Purchase handlers receive a bloated interface they never use (ISP violation).
- Purchase domain types are coupled to `CatalogAggregate` and related types (`ProductType`, `StockData`, `CatalogProperties`, `CatalogPurchaseRecord`).
- No architectural test currently protects Purchase ↔ Catalog the way `ModuleBoundariesTests` protects Leaflet ↔ KnowledgeBase, Logistics ↔ Manufacture, etc.

## Functional Requirements

### FR-1: Purchase-owned material catalog contract
Define a Purchase-owned interface and supporting read DTO that expose only the operations Purchase actually consumes from Catalog. The interface and DTO live in `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/`.

Files to create:
- `Contracts/IMaterialCatalogService.cs`
- `Contracts/MaterialInfo.cs` (Purchase-owned read DTO — see Data Model)
- Supporting Purchase-owned DTOs needed by stock analysis (see Data Model): `MaterialStockSnapshot`, `MaterialStockLevels`, `MaterialPurchaseSnapshot`, `MaterialProductType` enum.

Minimum surface area required (signatures may be refined during implementation):

```csharp
public interface IMaterialCatalogService
{
    Task<MaterialInfo?> GetByIdAsync(string productCode, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<string, MaterialInfo>> GetByIdsAsync(
        IEnumerable<string> productCodes,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<MaterialInfo>> GetAllAsync(CancellationToken cancellationToken);

    // Returns stock + pre-computed consumption snapshots for the given period, filtered
    // to Material and Goods product types. Replaces the per-item GetConsumed/GetTotalSold
    // calls currently issued from GetPurchaseStockAnalysisHandler.
    Task<IReadOnlyList<MaterialStockSnapshot>> GetStockAnalysisSnapshotsAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken);

    // Returns materials that have a Bill of Materials, with the data needed to drive
    // RecalculatePurchasePriceHandler (productCode, BoMId). Replaces GetAllAsync + filter
    // by HasBoM currently in that handler.
    Task<IReadOnlyList<MaterialBomReference>> GetMaterialsWithBomAsync(
        CancellationToken cancellationToken);
}
```

**Acceptance criteria:**
- `IMaterialCatalogService` is declared in `Anela.Heblo.Application.Features.Purchase.Contracts`.
- The interface references only Purchase-owned types and BCL types — **no** references to `Anela.Heblo.Domain.Features.Catalog.*` or `Anela.Heblo.Application.Features.Catalog.*`.
- All supporting DTOs (`MaterialInfo`, `MaterialStockSnapshot`, etc.) live under `Features/Purchase/Contracts/`.
- The interface is `public`; DTOs are `public sealed class` (per project rule: DTOs are classes, never records — see CLAUDE.md).
- Methods accept `CancellationToken` per global C# coding style.

### FR-2: Catalog-side adapter
Implement the contract in the Catalog module via an internal adapter that delegates to the existing `ICatalogRepository`.

Files to create:
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/PurchaseMaterialCatalogAdapter.cs`

The adapter is responsible for:
- Calling `ICatalogRepository.GetByIdAsync` / `GetAllAsync` (any required new method, see Open Questions).
- Projecting `CatalogAggregate` → `MaterialInfo` / `MaterialStockSnapshot` / `MaterialBomReference`.
- Performing the Material/Goods filter for `GetStockAnalysisSnapshotsAsync` (currently inline in the Purchase handler).
- Pre-computing per-period consumption (`GetConsumed` for Material, `GetTotalSold` for Goods) inside the snapshot projection, so Purchase no longer touches `CatalogAggregate` methods.
- Pre-computing the last-purchase summary (`MaterialPurchaseSnapshot`) for `GetPurchaseOrderByIdHandler`'s Note + line note rendering.

**Acceptance criteria:**
- Class is `internal sealed`, lives in `Anela.Heblo.Application.Features.Catalog.Infrastructure`.
- Class implements `IMaterialCatalogService`.
- Adapter does not leak `CatalogAggregate` or any Catalog domain type through the interface.
- `GetByIdsAsync` issues a single repository call (avoid N+1; if the repository lacks a bulk lookup, fetch from `GetAllAsync` once and project).

### FR-3: DI registration
Register the adapter binding in `CatalogModule.cs` (the provider module owns the binding, per the documented pattern).

Edit:
- `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs`

Add inside `AddCatalogModule`:

```csharp
services.AddScoped<IMaterialCatalogService, PurchaseMaterialCatalogAdapter>();
```

**Acceptance criteria:**
- `PurchaseModule` does **not** register `IMaterialCatalogService` (must be Catalog-owned per the pattern).
- Lifetime is `Scoped`, consistent with how `ICatalogRepository` is consumed today.
- Application starts without DI resolution errors; integration test that resolves all `IRequestHandler<,>` succeeds.

### FR-4: Migrate five Purchase handlers
Replace `ICatalogRepository` injection with `IMaterialCatalogService` in all five handlers and remove the `using Anela.Heblo.Domain.Features.Catalog;` statement (and any other Catalog-namespace imports introduced solely to support catalog usage) from each handler.

Per-handler scope:

**FR-4.1 `CreatePurchaseOrderHandler`**
- Replace `_catalogRepository.GetByIdAsync(lineRequest.MaterialId, ct)` with `_materialCatalog.GetByIdAsync(...)`.
- Use `material?.ProductName` from `MaterialInfo`.
- Acceptance: no `Catalog` namespace imports remain in the file.

**FR-4.2 `UpdatePurchaseOrderHandler`**
- Replace three `GetByIdAsync` calls inside the line loop and `MapToResponseAsync` with `IMaterialCatalogService.GetByIdsAsync` (batch) — preserves current behavior, fixes existing N+1.
- Acceptance: no `Catalog` namespace imports remain; integration behavior unchanged (snapshot tests still pass).

**FR-4.3 `GetPurchaseOrderByIdHandler`**
- Replace the per-`materialId` loop with `GetByIdsAsync(materialIds, ct)`.
- Replace `catalogItem.Note` access with `MaterialInfo.Note`.
- Acceptance: response payload (`CatalogNote` in line DTO) is byte-identical for existing fixtures.

**FR-4.4 `GetPurchaseStockAnalysisHandler`**
- Replace `_catalogRepository.GetAllAsync(ct)` + Material/Goods filter + per-item `GetConsumed`/`GetTotalSold`/property reads with a single call to `IMaterialCatalogService.GetStockAnalysisSnapshotsAsync(fromDate, toDate, ct)`.
- Move the Material/Goods filter into the adapter (FR-2).
- All `CatalogAggregate`, `ProductType`, `StockData`, `CatalogProperties`, `CatalogPurchaseRecord` references removed from the handler.
- The `AnalyzeStockItem` helper now takes `MaterialStockSnapshot` instead of `CatalogAggregate`.
- `StockSeverity`/`IStockSeverityCalculator` stay where they are (Purchase already owns the calculator). If `StockSeverity` lives in `Anela.Heblo.Domain.Features.Catalog`, it must be relocated or duplicated under `Purchase/Contracts` (see Open Questions).
- Acceptance: response identical to current implementation for the same fixtures (summary counts, item ordering, severities).

**FR-4.5 `RecalculatePurchasePriceHandler`**
- Replace `_catalogRepository.GetAllAsync` + `HasBoM` filter + `BoMId.HasValue` access with `IMaterialCatalogService.GetMaterialsWithBomAsync(ct)` (returns `IReadOnlyList<MaterialBomReference>` with `ProductCode` and `BoMId`).
- For the single-product path (`!string.IsNullOrEmpty(request.ProductCode)`), call `GetByIdAsync` and validate `HasBoM` + `BoMId.HasValue` against `MaterialInfo`.
- `IProductPriceErpClient` remains injected (it lives in `Domain.Features.Catalog.Price`; see Open Questions about whether this also requires decoupling — out of scope for this spec unless the architect decides otherwise).
- Acceptance: error codes (`InvalidValue`, `CatalogItemNotFound`) and counts (`TotalCount`, `SuccessCount`, `FailedCount`) match current behavior on existing fixtures.

### FR-5: Architectural test for Purchase → Catalog
Extend `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` with a new rule entry preventing Purchase types from referencing Catalog-owned namespaces, mirroring the Leaflet/Logistics/PackingMaterials rules already present.

Rule shape:

```csharp
new ModuleBoundaryRule(
    Name: "Purchase -> Catalog",
    InspectedNamespacePrefix: "Anela.Heblo.Application.Features.Purchase",
    ForbiddenNamespacePrefixes: new[]
    {
        "Anela.Heblo.Domain.Features.Catalog",
        "Anela.Heblo.Application.Features.Catalog",
        "Anela.Heblo.Persistence.Catalog",
    },
    Allowlist: PurchaseAllowlist),
```

`PurchaseAllowlist` starts empty; if any pre-existing dependency cannot be decoupled in this work (e.g., `IProductPriceErpClient` in `Domain.Features.Catalog.Price`), it is allowlisted with a justifying comment and a follow-up tracking note, matching the existing format in `LeafletAllowlist` / `LogisticsAllowlist`.

**Acceptance criteria:**
- Test `Consumer_types_should_not_reference_provider_owned_namespaces` runs for the new `Purchase -> Catalog` row and passes.
- Test fails (red) before FR-4 changes are applied and passes (green) after.

### FR-6: Unit tests for the adapter
Add unit tests under `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/` (mirroring the source path):

- `PurchaseMaterialCatalogAdapterTests`
  - Each interface method has at least one happy-path test using a stubbed `ICatalogRepository`.
  - `GetStockAnalysisSnapshotsAsync` filters to Material + Goods, computes per-period consumption correctly, and returns the last purchase snapshot.
  - `GetByIdsAsync` returns a dictionary keyed by `ProductCode` and omits missing IDs.

**Acceptance criteria:**
- Tests use xUnit + FluentAssertions + NSubstitute (or Moq, matching existing project convention).
- All new tests pass under `dotnet test`.

### FR-7: Existing handler tests updated
Existing tests in `backend/test/Anela.Heblo.Tests/Features/Purchase/...` that mocked `ICatalogRepository` must be updated to mock `IMaterialCatalogService` instead. Behavior assertions remain unchanged.

**Acceptance criteria:**
- No production-code Purchase handler test references `ICatalogRepository`.
- All previously-passing tests still pass.

## Non-Functional Requirements

### NFR-1: Performance
- `GetPurchaseStockAnalysisHandler` currently performs a single `GetAllAsync` followed by in-memory work. New flow must remain a single fetch — no per-item round trips introduced by the adapter.
- `UpdatePurchaseOrderHandler` and `GetPurchaseOrderByIdHandler` may *improve* by switching to `GetByIdsAsync` batch lookups; this is allowed but not required.
- Adapter must not introduce additional materialization of large collections beyond what `ICatalogRepository` already returns.

### NFR-2: Backwards compatibility
- All five handlers' public request/response contracts (DTOs in `Features/Purchase/Contracts/`) remain unchanged.
- HTTP-facing behavior (status codes, error codes, response shape) remains unchanged.
- No database migration is required.

### NFR-3: Security
- No new public endpoints, auth changes, or data exposure. Purely an internal refactor.

### NFR-4: Code quality
- All new code follows the global C# style rules: nullable reference types on, `CancellationToken` parameters, `sealed` adapters, no `dynamic`, async-await throughout.
- `dotnet build` and `dotnet format` clean before merge (per CLAUDE.md validation requirements).

## Data Model

All DTOs below are Purchase-owned, live under `Application/Features/Purchase/Contracts/`, and are **classes** (not records) to satisfy the project DTO rule.

### `MaterialInfo`
Read snapshot of a catalog item, exposing only the fields Purchase needs for non-analysis flows.

| Property | Type | Source (`CatalogAggregate`) | Used by |
|---|---|---|---|
| `ProductCode` | `string` | `ProductCode` (Id) | All |
| `ProductName` | `string` | `ProductName` | Create / Update / GetById |
| `Note` | `string?` | `Note` | GetById |
| `HasBoM` | `bool` | `HasBoM` | RecalculatePrice (single-product path) |
| `BoMId` | `int?` | `BoMId` | RecalculatePrice (single-product path) |

### `MaterialBomReference`
Lightweight result for `GetMaterialsWithBomAsync`.

| Property | Type |
|---|---|
| `ProductCode` | `string` |
| `BoMId` | `int` (non-nullable; only items with `HasBoM && BoMId.HasValue` are returned) |

### `MaterialStockSnapshot`
Pre-computed analysis input, returned only by `GetStockAnalysisSnapshotsAsync`. Filtered to `ProductType` ∈ {`Material`, `Goods`}.

| Property | Type | Source |
|---|---|---|
| `ProductCode` | `string` | `ProductCode` |
| `ProductName` | `string` | `ProductName` |
| `ProductNameNormalized` | `string` | `ProductNameNormalized` |
| `ProductType` | `MaterialProductType` (Purchase-owned enum: `Material`, `Goods`) | `Type` |
| `SupplierName` | `string?` | `SupplierName` |
| `MinimalOrderQuantity` | `string` | `MinimalOrderQuantity` |
| `IsMinStockConfigured` | `bool` | `IsMinStockConfigured` |
| `IsOptimalStockConfigured` | `bool` | `IsOptimalStockConfigured` |
| `Stock` | `MaterialStockLevels` | from `Stock` |
| `StockMinSetup` | `decimal` | `Properties.StockMinSetup` |
| `OptimalStockDaysSetup` | `int` | `Properties.OptimalStockDaysSetup` |
| `ConsumptionInPeriod` | `double` | pre-computed: `GetConsumed(from,to)` for Material, `GetTotalSold(from,to)` for Goods |
| `LastPurchase` | `MaterialPurchaseSnapshot?` | first item of `PurchaseHistory` ordered by `Date` desc |

### `MaterialStockLevels`

| Property | Type |
|---|---|
| `Available` | `decimal` |
| `Ordered` | `decimal` |
| `EffectiveStock` | `decimal` |

### `MaterialPurchaseSnapshot`

| Property | Type |
|---|---|
| `Date` | `DateTime` |
| `SupplierName` | `string` |
| `Amount` | `decimal` |
| `UnitPrice` | `decimal` |
| `TotalPrice` | `decimal` |

### `MaterialProductType` (enum)
`Material`, `Goods`. Purchase-owned. Maps from `Anela.Heblo.Domain.Features.Catalog.ProductType` inside the adapter.

## API / Interface Design
No HTTP/API changes. All work is internal to the .NET solution.

DI wiring after the change:

```
Purchase.UseCases.*Handler
    ↓ (depends on)
Purchase.Contracts.IMaterialCatalogService
    ↑ (implemented by, registered in CatalogModule)
Catalog.Infrastructure.PurchaseMaterialCatalogAdapter
    ↓ (delegates to)
Domain.Features.Catalog.ICatalogRepository
```

## Dependencies
- `Anela.Heblo.Application` (modified: new Contracts + Infrastructure + handler edits + module registration)
- `Anela.Heblo.Tests` (modified: new architectural rule, updated handler mocks, new adapter tests)
- No new NuGet packages.
- No database changes.

## Out of Scope
- Decoupling `IProductPriceErpClient` (Catalog Price domain) from `RecalculatePurchasePriceHandler` — Purchase still injects this directly. If the architect decides this must also move behind a Purchase-owned contract, allowlist it for now with a follow-up note.
- Splitting the fat `ICatalogRepository` interface itself. Other consumers continue to use it unchanged.
- Refactoring `IStockSeverityCalculator` or `StockSeverity` placement (already Purchase-side service; only impacted if `StockSeverity` enum currently lives in Catalog — see Open Questions).
- Adding more granular allowlist entries for other Purchase ↔ Catalog dependencies discovered during implementation; if any are found that are not in the five handlers, document and defer to a follow-up arch-review item.
- Eliminating the existing single-`GetAllAsync` + filter pattern in favor of a server-side filtered query.
- Changes to `PurchaseModule.cs` (no new bindings need to be registered there).

## Open Questions
1. **`StockSeverity` enum location.** `GetPurchaseStockAnalysisHandler` references `StockSeverity`. If this enum lives in `Anela.Heblo.Domain.Features.Catalog`, the architectural test (FR-5) will flag it. Resolution options: (a) move `StockSeverity` to a Purchase-owned namespace (it is conceptually a Purchase analysis concern), (b) duplicate it, or (c) add it to `PurchaseAllowlist` with a follow-up. Architect should pick. **Assumption used in this spec:** (a) — move it to `Purchase/Contracts/`.
2. **`IProductPriceErpClient` boundary.** It lives under `Anela.Heblo.Domain.Features.Catalog.Price`. Architectural test will flag `RecalculatePurchasePriceHandler` → `IProductPriceErpClient`. Resolution options: (a) allowlist with follow-up (matches Leaflet's `IDocumentTextExtractor` precedent), (b) introduce a second Purchase-owned contract `IPurchasePriceRecalculator` in the same PR. **Assumption used in this spec:** (a) — out of scope per "Out of Scope" above.
3. **Bulk `GetByIdsAsync` on `ICatalogRepository`.** The brief asserts `GetByIdsAsync` is already used by Purchase handlers, but the current handlers call `GetByIdAsync` in loops. Confirm whether a bulk repository method exists; if not, the adapter implements `GetByIdsAsync` by calling `GetAllAsync` once (cached path) or by issuing parallel `GetByIdAsync` calls. **Assumption used in this spec:** projecting from a single `GetAllAsync` is acceptable given Catalog already caches in memory; architect may prefer a real bulk repository method.
4. **Adapter visibility for tests.** Adapter is declared `internal sealed`. `Anela.Heblo.Tests` already has `InternalsVisibleTo` for the Application assembly. If not, add it. **Assumption used in this spec:** the attribute exists or will be added without extra ceremony.

## Status: HAS_QUESTIONS