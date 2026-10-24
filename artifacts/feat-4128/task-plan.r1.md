# Implementation Plan: Manufacture-owned ProductCatalogSnapshot (issue #4128)

**Goal:** `IManufactureCatalogSource` returns a Manufacture-owned `ProductCatalogSnapshot`; Manufacture no longer references `CatalogAggregate`, `CatalogSaleRecord` or `ConsumedMaterialRecord`; matching `ModuleBoundariesTests` allowlist entries are removed. No behaviour or API change.

**Architecture:** see `arch-review.r1.md` and `design.r1.md`. Provider adapter to change is `Features/Catalog/Infrastructure/CatalogManufactureCatalogSourceAdapter.cs` (not `Manufacture/Infrastructure/ManufactureCatalogSourceAdapter.cs`).

**Validation for every task:** `cd backend && dotnet build` and the tests named in the task; final task also runs `dotnet format` and the full `dotnet test`.

### task: add-snapshot-dtos-and-mapper

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Manufacture/Contracts/ProductCatalogSnapshot.cs`, `ProductStockSnapshot.cs`, `ProductPropertiesSnapshot.cs`, `ProductSalesRecordSnapshot.cs`, `ProductConsumptionRecordSnapshot.cs`, `ProductStockTakingSnapshot.cs`, `ProductKind.cs`, `StockTakingKind.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogAggregateSnapshotMapper.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogAggregateSnapshotMapperTests.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Manufacture/TestHelpers/ProductCatalogSnapshotBuilder.cs`

- [ ] Write failing mapper tests first: scalar fields, `Type` enum mapping for every `ProductType` value (use `Enum.GetValues` to assert each maps and unknown throws), stock and properties (defaults such as `ExpirationMonths == 12` preserved), sales/consumed/stock-taking history incl. empty lists, derived `ProductFamily`/`SizeCode`/`LastStockTaking` equal to the `CatalogAggregate` values for short, 3-char, 6-char and 10-char codes.
- [ ] Create the DTO classes per `design.r1.md` (classes, `init` setters, no records, no `Anela.Heblo.Domain.Features.Catalog` using). Start from the member set in the design, then extend as later tasks need.
- [ ] Implement `CatalogAggregateSnapshotMapper.ToSnapshot(CatalogAggregate)` (single pass, materialise each history list once) and `ToDomain(ProductStockTakingSnapshot)` returning `StockTakingRecord`.
- [ ] Create `ProductCatalogSnapshotBuilder` (fluent `With...` methods; defaults equal to `new CatalogAggregate()` defaults).
- [ ] Run `dotnet test --filter CatalogAggregateSnapshotMapperTests` - expect pass; `dotnet build` green (purely additive).
- [ ] Commit: `feat(manufacture): add ProductCatalogSnapshot contract DTOs and Catalog mapper`.

### task: flip-contract-and-migrate-production-consumers

**Files:**
- Modify: `Features/Manufacture/Contracts/IManufactureCatalogSource.cs` (return types; drop Catalog using and "deliberate leak" comment)
- Modify: `Features/Catalog/Infrastructure/CatalogManufactureCatalogSourceAdapter.cs` (map via `CatalogAggregateSnapshotMapper`; `GetByIdsAsync` keeps omitting missing ids; update class comment)
- Modify Manufacture services: `Services/BatchPlanningService.cs`, `ResidueDistributionCalculator.cs`, `ManufactureInventoryWriteDownService.cs`, `ManufactureSeverityCalculator.cs`, `IManufactureSeverityCalculator.cs`, `ManufactureAnalysisMapper.cs`, `IManufactureAnalysisMapper.cs`, `ConsumptionRateCalculator.cs`, `IConsumptionRateCalculator.cs`
- Modify Manufacture handlers under `UseCases/`: CreateManufactureOrder, UpdateManufactureOrder, UpdateManufactureOrderStatus, CalculatedBatchSize, CalculateBatchPlan, CalculateBatchByIngredient, GetManufactureProtocol, GetStockAnalysis, GetManufactureOutput, GetManufactureStockTakingHistory, GetSemiproductRecipePdf (all under `backend/src/Anela.Heblo.Application/Features/Manufacture/`)
- Leave `SubmitManufactureStockTakingHandler` for the stock-sync task (it only needs a temporary compile fix here: keep behaviour, see below)

- [ ] Swap the interface return types, then fix compile errors file by file replacing `CatalogAggregate` with `ProductCatalogSnapshot`, `CatalogSaleRecord` with `ProductSalesRecordSnapshot`, `ConsumedMaterialRecord` with `ProductConsumptionRecordSnapshot`. Add only the DTO members the compiler demands.
- [ ] `GetManufactureStockTakingHistoryHandler`: operate on `ProductStockTakingSnapshot`; map `StockTakingKind` to the existing Catalog `StockTakingType` for `ManufactureStockTakingHistoryItemDto` so the wire format is unchanged.
- [ ] `SubmitManufactureStockTakingHandler`: temporarily read `Type`, `HasLots` from the snapshot and comment out nothing else; the `product.SyncStockTaking(...)` call is replaced in the next-but-one task. To keep behaviour meanwhile, perform the history append through the Catalog side only in task `preserve-stock-taking-cache-patch`; do not leave the build broken: if needed, do this task and that task in one commit.
- [ ] Remove every `using Anela.Heblo.Domain.Features.Catalog*` that becomes unused in Manufacture (retain `.Stock` usings still needed by the ERP stock-taking submit code).
- [ ] `dotnet build` of the Application project must be green. Verify: `grep -rn CatalogAggregate backend/src/Anela.Heblo.Application/Features/Manufacture` returns nothing.
- [ ] Commit: `refactor(manufacture): consume ProductCatalogSnapshot instead of CatalogAggregate`.

### task: preserve-stock-taking-cache-patch

**Files:**
- Modify: `Features/Manufacture/Contracts/IManufactureCatalogStockSync.cs`
- Modify: `Features/Catalog/Infrastructure/CatalogManufactureStockSyncAdapter.cs`, and `CatalogCacheStore.cs` if an append-history helper is needed
- Modify: `Features/Manufacture/UseCases/SubmitManufactureStockTaking/SubmitManufactureStockTakingHandler.cs`
- Tests: existing `SubmitManufactureStockTakingHandlerTests.cs`, existing stock-sync adapter tests (find with `grep -rl CatalogManufactureStockSyncAdapter backend/test`)

Background: before this change the handler called `product.SyncStockTaking(record)` on the shared cached aggregate (appends to `StockTakingHistory`, sets `Stock.Erp`); `ApplyErpStockTakingAsync` patches stock and lots only. See Decision 4 in the architecture review.

- [ ] Write failing tests: (1) handler passes a `ProductStockTakingSnapshot` mapped from the ERP record to `SyncErpStockTakingAsync`; (2) Catalog sync adapter, after a sync, makes the record visible in the cached aggregate's `StockTakingHistory` once (idempotent on repeated call with same Id) and `Stock.Erp` equals the new amount.
- [ ] Extend `SyncErpStockTakingAsync` with `ProductStockTakingSnapshot record`; in the Catalog adapter map with `CatalogAggregateSnapshotMapper.ToDomain` and append to the cached aggregates (current and stale merged caches, under the same guards `ApplyErpStockTakingAsync` uses).
- [ ] Remove `product.SyncStockTaking(...)` from the handler; handler no longer calls any `CatalogAggregate` member. Keep `ErpStockTakingRequest/Lot` usage (out of scope).
- [ ] If investigation shows history is rebuilt elsewhere making the append redundant, drop the extra argument instead and prove unchanged behaviour with a test; note the finding in the PR.
- [ ] Run `dotnet test --filter "SubmitManufactureStockTaking|StockSync"` - pass.
- [ ] Commit: `refactor(manufacture): move stock-taking history cache patch behind sync contract`.

### task: migrate-manufacture-tests-to-snapshot

**Files:** the ~20 test files under `backend/test/Anela.Heblo.Tests/Features/Manufacture/` that mock `IManufactureCatalogSource` or build `CatalogAggregate` (find with `grep -rln "CatalogAggregate" backend/test/Anela.Heblo.Tests/Features/Manufacture`), plus `ManufactureInventoryWriteDownServiceTests`, `ResidueDistributionCalculatorTests`, `BatchPlanningServiceTests`, `ConsumptionRateCalculator` tests.

- [ ] Replace `new CatalogAggregate { ... }` in Manufacture tests with `ProductCatalogSnapshotBuilder`; replace Catalog sale/consumed/stock-taking record fixtures with the snapshot record types.
- [ ] Keep assertions unchanged (behaviour must be identical); do not weaken any test.
- [ ] `dotnet test backend --filter "FullyQualifiedName~Features.Manufacture|FullyQualifiedName~Features.Catalog"` - all pass.
- [ ] Commit: `test(manufacture): use ProductCatalogSnapshot in Manufacture tests`.

### task: shrink-module-boundaries-allowlist-and-docs

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`
- Modify (if they mention the contract): `docs/processes/module-manufacture*.md`, `docs/architecture/development_guidelines.md` example list

- [ ] Delete from `ManufactureCatalogAllowlist` all entries targeting `Catalog.CatalogAggregate`, `Catalog.Sales.CatalogSaleRecord`, `Catalog.ConsumedMaterials.ConsumedMaterialRecord`, and the `Catalog.Stock.StockTakingRecord` / `StockTakingType` entries that were only reachable via aggregate history (history handler `+<>c`, `CalculatedBatchSizeHandler+<>c`, `CalculateBatchByIngredientHandler+<>c`).
- [ ] Run `dotnet test --filter ModuleBoundariesTests`; for each remaining failure decide: is it a real remaining reference to fix, or a legitimate out-of-scope entry (ERP stock-taking types, `CatalogManufactureRecord`, `ManufactureStockTakingHistoryItemDto -> StockTakingType`)? Re-add only the latter, with a comment saying why and that it is a separate follow-up. Never re-add a `CatalogAggregate` entry.
- [ ] Rewrite the allowlist header comment (it currently describes the ProductCatalogSnapshot follow-up) to describe the remaining residual entries.
- [ ] If a process doc was touched run `python3 scripts/process-docs/check.py index`.
- [ ] Commit: `test(architecture): drop Manufacture->Catalog CatalogAggregate allowlist entries`.

### task: final-validation

- [ ] `cd backend && dotnet build && dotnet format --verify-no-changes` (run `dotnet format` and commit if needed).
- [ ] `dotnet test` for the whole backend solution; all pass.
- [ ] Confirm OpenAPI unchanged: build the API project and verify the generated TypeScript client shows no diff (`git status frontend/src/api`).
- [ ] `grep -rn "Domain.Features.Catalog" backend/src/Anela.Heblo.Application/Features/Manufacture` lists only the documented residual (ERP stock-taking types, `CatalogManufactureRecord`, `StockTakingType` in the history DTO).
- [ ] File a follow-up GitHub issue (or note in PR description) for the residual allowlist entries.
