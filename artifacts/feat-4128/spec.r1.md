# Specification: Manufacture-owned ProductCatalogSnapshot for IManufactureCatalogSource

## Summary
`IManufactureCatalogSource`, Manufacture's cross-module contract to Catalog, currently returns the Catalog-owned domain type `CatalogAggregate`. This feature introduces a Manufacture-owned, read-only `ProductCatalogSnapshot` DTO (plus small nested record DTOs), changes the contract to return it, maps `CatalogAggregate` to it in the Catalog-side adapter, and removes the `CatalogAggregate`-related Catalog domain imports from Manufacture and the matching `ModuleBoundariesTests` allowlist entries. Behaviour of all Manufacture endpoints must remain unchanged.

## Background
Arch-review issue #4128 (module: Manufacture). The cross-module contract pattern in `docs/architecture/development_guidelines.md` requires the consumer module to own both the interface and its return DTOs. `IManufactureCatalogSource` (in `Features/Manufacture/Contracts/`) is implemented by `CatalogManufactureCatalogSourceAdapter` (in `Features/Catalog/Infrastructure/`) which delegates to `ICatalogRepository`. Its XML comment acknowledges returning `CatalogAggregate` as a "deliberate pragmatic leak" with a follow-up to introduce `ProductCatalogSnapshot`. The leak is allowlisted in `ModuleBoundariesTests` (`ManufactureCatalogAllowlist`), so Catalog changes ripple into 20+ Manufacture files unchecked.

Consumers of the contract (production code): `BatchPlanningService`, `ResidueDistributionCalculator`, `ManufactureInventoryWriteDownService`, `ManufactureSeverityCalculator`/`IManufactureSeverityCalculator`, `ManufactureAnalysisMapper`/`IManufactureAnalysisMapper`, and handlers `CreateManufactureOrder`, `UpdateManufactureOrder`, `UpdateManufactureOrderStatus`, `CalculatedBatchSize`, `CalculateBatchPlan`, `CalculateBatchByIngredient`, `GetManufactureProtocol`, `GetManufacturingStockAnalysis`, `GetManufactureOutput`, `GetManufactureStockTakingHistory`, `SubmitManufactureStockTaking`, `GetSemiproductRecipePdf`. Members of `CatalogAggregate` actually read by Manufacture (from code survey) include: `ProductCode`, `ProductName`, `ProductNameNormalized`, `Type`, `ProductFamily`, `SizeCode`, `MinimalManufactureQuantity`, `NetWeight`, `HasLots`, `Stock` (`Total`, `Available`, `Planned`, `Erp`, `Eshop`, `Transport`, `Reserve`, `Quarantine`, `Manufactured`, `PrimaryStockSource`), `Properties` (`ExpirationMonths`, `OptimalStockDaysSetup`, `StockMinSetup`, `BatchSize`, `AllowedResiduePercentage`), `SalesHistory` (Date, AmountB2B, AmountB2C), `ConsumedHistory` (Date, Amount), `StockTakingHistory` (Id, Type, Code, AmountNew, AmountOld, Date, User, Error), and `SyncStockTaking(...)`. The implementer must confirm the exact list by compiling after the type swap.

## Functional Requirements

### FR-1: Manufacture-owned snapshot DTO
Add `ProductCatalogSnapshot` (class, not record, per project DTO rule) in `backend/src/Anela.Heblo.Application/Features/Manufacture/Contracts/`, containing only the data Manufacture reads, plus nested Manufacture-owned DTOs (e.g. `ProductStockSnapshot`, `ProductPropertiesSnapshot`, `ProductSalesRecordSnapshot`, `ProductConsumptionRecordSnapshot`, `ProductStockTakingSnapshot`) and a Manufacture-owned `ProductKind` (or equivalent) enum mirroring the values of `ProductType` that Manufacture checks. The DTO namespace/types must not reference `Anela.Heblo.Domain.Features.Catalog`.
**Acceptance criteria:**
- No type in `Features/Manufacture/Contracts/ProductCatalogSnapshot*` references any `Anela.Heblo.Domain.Features.Catalog.*` type.
- The DTO exposes derived values Manufacture needs (e.g. `ProductFamily`, `SizeCode`, `LastStockTaking`) either as computed properties on the DTO or populated by the adapter, so behaviour matches `CatalogAggregate`.

### FR-2: Contract change
`IManufactureCatalogSource.GetByIdAsync/GetByIdsAsync/GetAllAsync` return `ProductCatalogSnapshot?`, `IReadOnlyDictionary<string, ProductCatalogSnapshot>`, `IEnumerable<ProductCatalogSnapshot>`. `GetProductNameSuffixesAsync` and `RefreshPlannedDataAsync` are unchanged. The `using Anela.Heblo.Domain.Features.Catalog` and the "deliberate leak" comment are removed from the interface.
**Acceptance criteria:**
- `IManufactureCatalogSource.cs` has no Catalog domain using.

### FR-3: Catalog-side mapping
`CatalogManufactureCatalogSourceAdapter` (Catalog.Infrastructure) maps `CatalogAggregate` to `ProductCatalogSnapshot` via a dedicated mapper (e.g. `CatalogAggregateSnapshotMapper`). Mapping is lossless for every field Manufacture consumes. (Note: the issue text names `ManufactureCatalogSourceAdapter` in Manufacture.Infrastructure; that class implements the opposite-direction `ICatalogManufactureSource`. The correct class to change is the Catalog-side adapter.)
**Acceptance criteria:**
- Unit tests for the mapper cover scalar fields, stock, properties, sales/consumed history and stock-taking history, null `Properties`/empty collections.

### FR-4: Migrate Manufacture consumers
All Manufacture services and handlers listed above consume `ProductCatalogSnapshot`. `IManufactureSeverityCalculator`, `IManufactureAnalysisMapper`, `ConsumptionRateCalculator`/`IConsumptionRateCalculator` take Manufacture-owned types instead of `CatalogAggregate`, `CatalogSaleRecord`, `ConsumedMaterialRecord`.
**Acceptance criteria:**
- `grep CatalogAggregate backend/src/Anela.Heblo.Application/Features/Manufacture` returns nothing.
- Existing handler/service unit tests are updated and pass; response payloads are byte-for-byte unchanged for equal inputs.

### FR-5: Stock-taking submission path
`SubmitManufactureStockTakingHandler` currently calls `product.SyncStockTaking(record)` on the returned aggregate, and then `IManufactureCatalogStockSync.SyncErpStockTakingAsync`. The snapshot is read-only, so the in-memory mutation must be removed or replaced. The implementer must verify (and document in the PR) whether the aggregate returned by `ICatalogRepository` is a shared cached instance (in which case the mutation was an observable cache patch and its effect must be preserved inside `IManufactureCatalogStockSync`/the Catalog adapter) or a copy (in which case it is dead code).
**Acceptance criteria:**
- Stock level and lots visible via `IManufactureCatalogSource` after a successful submit are identical to before the change (test covers it).
- Handler no longer calls any `CatalogAggregate` member.

### FR-6: Remove allowlist entries and Catalog imports
Remove from `ManufactureCatalogAllowlist` in `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` every entry that exists solely because of `CatalogAggregate`, `CatalogSaleRecord` and `ConsumedMaterialRecord` reachability, and remove the corresponding `using Anela.Heblo.Domain.Features.Catalog*` lines from Manufacture sources. `ModuleBoundariesTests` must pass.
**Acceptance criteria:**
- No allowlist entry with target `...Catalog.CatalogAggregate`, `...Catalog.Sales.CatalogSaleRecord` or `...Catalog.ConsumedMaterials.ConsumedMaterialRecord` remains.
- Comments in the allowlist and in `CatalogManufactureCatalogSourceAdapter` that refer to the "deliberate leak" are updated/removed.

## Non-Functional Requirements

### NFR-1: Performance
`GetAllAsync` is used by stock analysis over the full catalog. Mapping must be a single pass, must not deep-copy history collections that Manufacture does not use for a given call path beyond what is needed, and must not introduce extra repository calls. No measurable regression in `GetManufacturingStockAnalysis` (implementer to sanity check with the existing tests/benchmarks; allocate only the history lists actually consumed, or map lazily).

### NFR-2: Security
No change to authorization or data exposure; snapshot is internal and not an API response type (not exposed through OpenAPI).

### NFR-3: Compatibility
No API/OpenAPI change; generated TypeScript client must be unchanged. No DB migration.

## Data Model
New in-memory DTOs only (see FR-1). No persisted entities change.

## API / Interface Design
Only the internal C# interface `IManufactureCatalogSource` changes (FR-2). Mapper is internal to Catalog.Infrastructure. No HTTP or MCP surface changes.

## Dependencies
- `ICatalogRepository`, `CatalogAggregate` (Catalog domain) remain unchanged.
- Docs: `docs/architecture/development_guidelines.md` (contract pattern); `docs/processes/` module doc for Manufacture if it mentions the contract (CLAUDE.md rule; run `python3 scripts/process-docs/check.py index` if a doc is touched).

## Out of Scope
- Other Manufacture-to-Catalog allowlist entries not caused by `CatalogAggregate` reachability: `ErpStockTakingLot`, `ErpStockTakingRequest`, `IErpStockDomainService`, `StockTakingRecord` use in `SubmitManufactureStockTakingHandler` (Catalog.Stock domain service for ERP writes), and `CatalogManufactureRecord` in `ManufactureCatalogSourceAdapter`/`ProductionActivityAnalyzer`. These stay allowlisted (with updated comments) and should be tracked separately.
- Catalog -> Manufacture allowlist items.
- Changing behaviour of any Manufacture calculation.

## Open Questions
None. Assumptions: (1) snapshot is one DTO shared by all methods rather than per-use-case projections; (2) `ProductType` is mirrored by a Manufacture-owned enum mapped by name; (3) the stock-taking Erp types are out of scope as noted above.

## Status: COMPLETE
