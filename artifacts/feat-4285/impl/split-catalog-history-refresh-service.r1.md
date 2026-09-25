# Implementation: split-catalog-history-refresh-service

## What was implemented

Extracted the time-windowed "history" refresh operations out of `CatalogDataRefreshService`
into a new, narrower `CatalogHistoryRefreshService` class, per the task context. The new
service owns sales, set-parts (bundle composition), purchase history, consumed-material
history, and manufacture history refresh — with the exact same bodies, doc comments, and
resilience/retry/logging behavior as the originals. `CatalogDataRefreshService.cs` itself
was intentionally left untouched at this step (its own removal is a separate task,
`remove-old-refresh-service-and-verify`, per the task-context set for this feature), so both
classes currently coexist with duplicate method bodies.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogHistoryRefreshService.cs`
  — new sealed class with a 10-parameter constructor (`ICatalogSalesClient`,
  `ICatalogSetPartsClient`, `IPurchaseHistoryClient`, `IConsumedMaterialsClient`,
  `ICatalogManufactureSource`, `ICatalogResilienceService`, `TimeProvider`,
  `IOptions<DataSourceOptions>`, `CatalogCacheStore`, `ILogger<CatalogHistoryRefreshService>`)
  and five public methods: `RefreshSalesData`, `RefreshSetPartsData` (+ private
  `FetchSetPartsPerBundleAsync` helper), `RefreshPurchaseHistoryData`,
  `RefreshConsumedHistoryData`, `RefreshManufactureHistoryData`. Copied verbatim from the
  task context's Step 1 code block, including the existing XML doc comments explaining the
  hydration-tier ordering constraint and per-bundle fetch rationale.
- `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogHistoryRefreshServiceTests.cs`
  — new test class moving the 6 relevant test cases verbatim from
  `CatalogDataRefreshServiceTests.cs`: `RefreshSalesData_WhenResilienceThrows_RetainsStaleCacheAndLogsWarning`,
  `RefreshSetPartsData_FetchesPartsOnlyForBundleCodedProducts`,
  `RefreshSetPartsData_WhenOneBundleFails_KeepsPartsFromTheOthers`,
  `RefreshSetPartsData_FetchesEachBundleSeparately`,
  `RefreshSetPartsData_WhenResilienceThrows_RetainsStaleCacheAndLogsWarning`,
  `RefreshSetPartsData_WhenNoBundleCodedProductsExist_RetainsExistingCacheAndLogsWarning`,
  plus a `CreateService` helper scoped to this class's 7 constructor parameters.

## Deviation from the task-context spec

The test file's usings block, as given in the task context Step 3, is missing
`using Anela.Heblo.Domain.Features.Catalog.ConsumedMaterials;`, which is required for the
`IConsumedMaterialsClient` parameter type used in `CreateService`. Without it the file fails
to compile with `CS0246: The type or namespace name 'IConsumedMaterialsClient' could not be
found`. Added the missing using — no other change to the spec's given code.

## Tests

- `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogHistoryRefreshServiceTests.cs`
  covers: stale-cache retention + warning log when sales resilience throws; bundle-only
  fetching for set parts; partial-failure isolation across bundles; one resilience call per
  bundle (not a single batched call); stale-cache retention + warning log when set-parts
  resilience throws; and no-bundle-coded-products guard retaining the existing cache and
  logging a warning.

## How to verify

```bash
cd backend
dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CatalogHistoryRefreshServiceTests"
```

Ran both: build succeeded (0 errors, only pre-existing unrelated warnings), and
`Passed! - Failed: 0, Passed: 6, Skipped: 0, Total: 6`.

## Notes

- `CatalogDataRefreshService.cs` was not modified or removed — that is explicitly a later
  task (`remove-old-refresh-service-and-verify`) in this feature's task-context set, and
  `CatalogRepository`/`CatalogModule` wiring is likewise untouched here (also a later task,
  `wire-catalog-repository-and-module`). This step only adds the new standalone service.
- No commit was made from inside this developer step; the pipeline orchestrator handles
  staging/committing per its two-line staging rule after this artifact is written.

## PR Summary

Extracted sales, set-parts, purchase-history, consumed-history, and manufacture-history
refresh logic from `CatalogDataRefreshService` into a new standalone
`CatalogHistoryRefreshService`, along with its own test file covering the 6 relevant
existing test cases moved verbatim. `CatalogDataRefreshService` itself is left in place for
now; removing it and rewiring DI registrations are separate, later tasks in this feature.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogHistoryRefreshService.cs` — new service class (sales/set-parts/purchase-history/consumed-history/manufacture-history refresh)
- `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogHistoryRefreshServiceTests.cs` — new test file (6 tests moved from `CatalogDataRefreshServiceTests.cs`), with a missing `using` added to fix a compile error in the task-context's given test code

## Status
DONE
