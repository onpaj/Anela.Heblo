# Architecture Review: Manufacture-owned ProductCatalogSnapshot for IManufactureCatalogSource

## Skip Design: true
Backend-only refactor of an internal C# contract. No UI, no API or OpenAPI change.

## Architectural Fit Assessment
Fully aligned with the cross-module contract rule in `docs/architecture/development_guidelines.md` (consumer owns interface and DTOs; provider module owns the adapter that maps). The repo already follows this for `DataQuality` (`DqtInvoiceSnapshot`, mapped in `Invoices.Infrastructure`) and `Purchase` (`PurchaseOrder`-side snapshots), and `ModuleBoundariesTests` already encodes the empty-allowlist end state for those. Integration points verified in code:
- Contract: `Features/Manufacture/Contracts/IManufactureCatalogSource.cs`.
- Provider adapter: `Features/Catalog/Infrastructure/CatalogManufactureCatalogSourceAdapter.cs` (internal sealed, delegates to `ICatalogRepository`; DI in `CatalogModule`). NOTE: the issue's step 3 names `Manufacture/Infrastructure/ManufactureCatalogSourceAdapter`; that class implements the reverse `ICatalogManufactureSource` and must NOT be changed for this.
- `ICatalogRepository.GetByIdAsync/GetByIdsAsync` return **shared, cached `CatalogAggregate` instances** (`_cacheStore.GetCatalogData()`), not copies. This matters for the stock-taking path (below).
- Related contract already in the right shape: `IManufactureCatalogStockSync` / `CatalogManufactureStockSyncAdapter`.

## Proposed Architecture

### Component Overview
```
Manufacture module (consumer, no Catalog domain refs for aggregates)
  Contracts/
    IManufactureCatalogSource  --returns-->  ProductCatalogSnapshot (+ nested DTOs, ProductKind enum)
    IManufactureCatalogStockSync (extended: carries ProductStockTakingSnapshot)
  Services/UseCases/Handlers  --consume-->  ProductCatalogSnapshot only

Catalog module (provider)
  Infrastructure/
    CatalogManufactureCatalogSourceAdapter  -- uses --> CatalogAggregateSnapshotMapper (internal static)
    CatalogManufactureStockSyncAdapter      -- applies stock-taking history to cached aggregate
```
Dependency direction stays Catalog.Infrastructure -> Manufacture.Contracts (provider depends on consumer contract), as today.

### Key Design Decisions

#### Decision 1: One shared snapshot type, not per-use-case projections
**Options considered:** (a) one `ProductCatalogSnapshot` for all three methods; (b) several narrow DTOs per handler.
**Chosen approach:** (a). The interface has three methods returning the same entity and 15+ consumers read overlapping subsets; (b) would explode the surface for no coupling gain since the DTO is Manufacture-owned either way.
**Rationale:** Matches the issue's suggested fix and the `Dqt*Snapshot` precedent; the coupling to fix is ownership, not field count.

#### Decision 2: Snapshot is an immutable-by-convention class with init-only/get-only members and Manufacture-owned nested types
**Options considered:** C# record vs class; reusing Catalog `StockData`, `CatalogProperties`, `CatalogSaleRecord`, `ConsumedMaterialRecord`, `StockTakingRecord`.
**Chosen approach:** Plain classes (project rule: DTOs are classes, never records), `init` setters. New Manufacture-owned types: `ProductStockSnapshot`, `ProductPropertiesSnapshot`, `ProductSalesRecordSnapshot` (Date, AmountB2B, AmountB2C), `ProductConsumptionRecordSnapshot` (Date, Amount), `ProductStockTakingSnapshot` (Id, Type, Code, AmountNew, AmountOld, Date, User, Error), enums `ProductKind` and `StockTakingKind`. Only include members the compiler proves are used.
**Rationale:** Reusing any Catalog type would keep the allowlist entries alive and defeat the purpose.

#### Decision 3: Derived members computed in the snapshot from copied primitives
`ProductFamily`, `SizeCode`, `LastStockTaking` are pure functions of copied data; implement them on the snapshot (small, tested) rather than copying duplicated state, so call sites change from `x.ProductFamily` to `x.ProductFamily` unchanged. Do not copy Catalog's `Left` extension; use `Substring`/`Math.Min` equivalent with a unit test pinned to Catalog behaviour.

#### Decision 4: Stock-taking submit path keeps the cache-patch behaviour via the sync contract
`SubmitManufactureStockTakingHandler` currently calls `product.SyncStockTaking(record)` on the shared cached aggregate, which (a) appends the record to `StockTakingHistory` and (b) sets `Stock.Erp`. `CatalogCacheStore.ApplyErpStockTakingAsync` (via `IManufactureCatalogStockSync`) patches the stock and lots in caches but not the history list. Removing the call would silently stop the new record appearing in `GetManufactureStockTakingHistory` and `LastStockTaking` until the next refresh.
**Chosen approach:** Extend `IManufactureCatalogStockSync.SyncErpStockTakingAsync` with an additional `ProductStockTakingSnapshot record` argument (Manufacture-owned). `CatalogManufactureStockSyncAdapter` maps it back to `StockTakingRecord`, and after `ApplyErpStockTakingAsync` appends the history record to the cached aggregate (via the repository/cache store, in the same code path that already patches the merged cache). The handler maps the `StockTakingRecord` returned by `IErpStockDomainService` to the snapshot. Handler no longer touches any aggregate member.
**Rationale:** Preserves observable behaviour; keeps the Catalog write semantics inside Catalog. Fallback if the implementer finds `StockTakingHistory` is rebuilt by a different cache path making the append unnecessary: drop the extra argument and document it in the PR, with a test proving history/last stock-taking unchanged.

#### Decision 5: Allowlist scope
Remove entries whose only cause is aggregate reachability: all `-> CatalogAggregate`, `-> Catalog.Sales.CatalogSaleRecord`, `-> Catalog.ConsumedMaterials.ConsumedMaterialRecord`, and `-> Catalog.Stock.StockTakingRecord`/`StockTakingType` entries for the history handlers, `CalculatedBatchSizeHandler+<>c` and `CalculateBatchByIngredientHandler+<>c`. Keep (and re-comment) entries for the ERP stock-taking write types in `SubmitManufactureStockTakingHandler` (`ErpStockTakingLot`, `ErpStockTakingRequest`, `IErpStockDomainService`, `StockTakingRecord` for the ERP call) and `CatalogManufactureRecord` (`ManufactureCatalogSourceAdapter`, `ProductionActivityAnalyzer`); those are a distinct leak and out of scope. Compiler-generated state-machine entries disappear naturally; run the test to find stragglers rather than guessing names.

## Implementation Guidance

### Directory / Module Structure
- NEW `backend/src/Anela.Heblo.Application/Features/Manufacture/Contracts/ProductCatalogSnapshot.cs` (and sibling files for nested DTOs/enums, one type per file).
- NEW `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogAggregateSnapshotMapper.cs` (internal static).
- EDIT `IManufactureCatalogSource.cs`, `IManufactureCatalogStockSync.cs`, `CatalogManufactureCatalogSourceAdapter.cs`, `CatalogManufactureStockSyncAdapter.cs`.
- EDIT Manufacture consumers: `Services/BatchPlanningService.cs`, `ResidueDistributionCalculator.cs`, `ManufactureInventoryWriteDownService.cs`, `ManufactureSeverityCalculator.cs` + interface, `ManufactureAnalysisMapper.cs` + interface, `ConsumptionRateCalculator.cs` + interface, and handlers under `UseCases/` (list in spec).
- EDIT tests: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`; ~20 Manufacture test files that mock `IManufactureCatalogSource`; add `ProductCatalogSnapshotBuilder` test helper and mapper tests under `Tests/Features/Catalog/Infrastructure/`.

### Interfaces and Contracts
`IManufactureCatalogSource` returns `ProductCatalogSnapshot?`, `IReadOnlyDictionary<string, ProductCatalogSnapshot>`, `IEnumerable<ProductCatalogSnapshot>`; other two members unchanged. Mapping must preserve null/absent semantics (`GetByIdsAsync` still omits missing ids). Collections exposed as `IReadOnlyList<>`; mapper materialises only once.

### Data Flow
Handler -> `IManufactureCatalogSource` -> `CatalogManufactureCatalogSourceAdapter` -> `ICatalogRepository` (cached aggregate) -> `CatalogAggregateSnapshotMapper` -> `ProductCatalogSnapshot` -> handler/service. Stock-taking submit: handler -> ERP domain service -> record -> `IManufactureCatalogStockSync.SyncErpStockTakingAsync(code, amount, snapshotRecord)` -> Catalog cache patch (stock, lots, history).

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Behaviour drift from missed field in mapping | High | Add compile-driven field inventory; mapper unit tests; keep all existing handler tests green with a builder defaulting to the same values as `new CatalogAggregate()` defaults (e.g. `ExpirationMonths = 12`) |
| Lost stock-taking history patch (shared cached aggregate no longer mutated) | High | Decision 4; test that history and `LastStockTaking` reflect the new record after submit |
| `GetAllAsync` full-catalog mapping cost (stock analysis) | Medium | Single pass; copy only used history collections (sales/consumed/stock-taking); avoid LINQ-heavy double enumeration; verify stock analysis tests and consider a quick timing check |
| Derived value mismatch (`ProductFamily`, `SizeCode`) | Medium | Pin with tests against `CatalogAggregate` outputs |
| Large mechanical edit across ~40 files causes merge conflicts | Medium | Do contract+mapper first, then consumers in small tasks, keep build green per task by staging with a compatibility step (see plan ordering) |
| Allowlist test names of compiler-generated types fragile | Low | Remove by running the test and deleting what no longer appears; do not add new entries |

## Specification Amendments
- FR-3: correct adapter is `CatalogManufactureCatalogSourceAdapter` (Catalog.Infrastructure), not `ManufactureCatalogSourceAdapter`.
- FR-5 resolved: use Decision 4 (extend `IManufactureCatalogStockSync` with a Manufacture-owned record).
- Out of Scope clarified: `ErpStock*` types and `CatalogManufactureRecord` allowlist entries remain; a follow-up issue should be filed for them.

## Prerequisites
None. No migration, config or infrastructure. After merge: if `docs/processes/module-manufacture*.md` references the contract or the allowlist, update it in the same PR and run `python3 scripts/process-docs/check.py index`.
