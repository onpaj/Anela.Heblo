## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/` — no `CatalogMetaRefreshServiceTests.cs` was created. This is not a coverage gap (the original `CatalogDataRefreshServiceTests.cs` had zero test cases exercising `RefreshAttributesData`/`RefreshLotsData`/`RefreshEshopPricesData`/`RefreshErpPricesData`/`RefreshEshopUrlData`, so there was nothing to move), but FR-4 of the spec literally calls for "four new test files ... mirroring the new class boundaries." Consider adding a minimal `CatalogMetaRefreshServiceTests.cs` (even a single smoke test) in a future pass purely for structural symmetry with the other three refresh services — not required for correctness or 1:1 test-case preservation, which both hold.

### Notes (verification performed)
- Confirmed all 18 (+2 indirect) `Refresh*` methods from the deleted `CatalogDataRefreshService` exist verbatim (identical bodies, comments, and log messages) on exactly one of the four new classes (`CatalogHistoryRefreshService`, `CatalogStockRefreshService`, `CatalogMetaRefreshService`, `CatalogReferenceRefreshService`), matching the FR-1 grouping exactly.
- Constructor parameter counts: History=10, Stock=8, Meta=8, Reference=5 — all ≤ 10 per acceptance criteria.
- `ICatalogManufactureSource` is injected into both `CatalogHistoryRefreshService` and `CatalogStockRefreshService`, matching the spec's explicitly accepted sharing exception.
- `CatalogRepository`'s 19 `Refresh*Data` delegate methods were checked one-by-one against the new services and each routes to the correct owner (e.g. `RefreshSalesData`→History, `RefreshErpStockData`→Stock, `RefreshLotsData`→Meta, `RefreshStockTakingData`→Reference). `RefreshMarginData` (not delegated) is untouched.
- `CatalogModule.AddCatalogModule` registers all four new services as `Transient`, no longer registers `CatalogDataRefreshService`. `RegisterBackgroundRefreshTasks` has zero diff (confirmed via the full diff — untouched).
- `grep -rn "CatalogDataRefreshService" backend/` returns no hits — no dangling references to the deleted class anywhere in the codebase.
- All 10 original `[Fact]` test cases from `CatalogDataRefreshServiceTests.cs` (renamed to `CatalogHistoryRefreshServiceTests.cs`) are accounted for 1:1 across the split: 6 stayed in `CatalogHistoryRefreshServiceTests.cs` (sales + 5 set-parts tests), 3 moved to `CatalogReferenceRefreshServiceTests.cs` (manufacture-difficulty ×2 + manufacture-cost), 1 moved to `CatalogStockRefreshServiceTests.cs` (ERP stock). Original file no longer exists (git-renamed).
- All four production-code and four test-code constructor call sites (`CatalogRepositoryTests`, `CatalogRepositoryCacheOptimizationTests`, `CatalogRepositoryStaleDataAndChangesPendingTests`, `MarginCostWindowAlignmentTests`) pass arguments in the exact order each new constructor declares them.
- `dotnet build Anela.Heblo.sln -c Release` — 0 errors (pre-existing warnings only, none introduced by this diff).
- `dotnet test --filter "FullyQualifiedName~RefreshService"` — 20/20 passed.
- `dotnet test --filter "FullyQualifiedName~Catalog"` — 1021/1025 passed; the 4 failures are pre-existing `GetStockUpOperationsSummaryIntegrationTests` cases that require Docker/Testcontainers (unavailable in this sandboxed environment) and are unrelated to this change (they use Postgres containers, not the refresh services).
