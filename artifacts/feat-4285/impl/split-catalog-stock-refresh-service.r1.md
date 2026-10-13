# Implementation: split-catalog-stock-refresh-service

## What was implemented

Extracted the current stock/inventory position refresh operations out of
`CatalogDataRefreshService` into a new, narrower `CatalogStockRefreshService` class, per the
task context. The new service owns ERP stock, eshop stock, in-transport, in-reserve/in-quarantine,
ordered quantities, and manufactured/planned inventory refresh — with the exact same bodies and
resilience/logging behavior as the originals. `CatalogDataRefreshService.cs` itself was
intentionally left untouched at this step (its removal is a separate task,
`remove-old-refresh-service-and-verify`, per the task-context set for this feature), so the
classes currently coexist with duplicate method bodies (same pattern as the prior
`split-catalog-history-refresh-service` task).

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogStockRefreshService.cs`
  — new sealed class with an 8-parameter constructor (`IErpStockClient`, `IEshopStockClient`,
  `ICatalogTransportSource`, `ICatalogPurchaseSource`, `ICatalogManufactureSource`,
  `ICatalogResilienceService`, `CatalogCacheStore`, `ILogger<CatalogStockRefreshService>`) and
  seven public methods: `RefreshErpStockData`, `RefreshEshopStockData`, `RefreshTransportData`,
  `RefreshReserveData`, `RefreshOrderedData`, `RefreshManufacturedData`, `RefreshPlannedData`.
  Copied verbatim from the task context's Step 1 code block, matching the corresponding methods
  in `CatalogDataRefreshService.cs` byte-for-byte.
- `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogStockRefreshServiceTests.cs`
  — new test class moving the one relevant test case verbatim from
  `CatalogDataRefreshServiceTests.cs`: `RefreshErpStockData_WritesToCacheStore`, plus a
  `CreateService` helper scoped to this class's 8 constructor parameters.

## Tests

- `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogStockRefreshServiceTests.cs`
  covers: `RefreshErpStockData` writing fetched ERP stock records into the cache store via the
  resilience wrapper.

## How to verify

```bash
cd backend
dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CatalogStockRefreshServiceTests"
```

Ran both: build succeeded (0 errors, only pre-existing unrelated warnings), and
`Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1`.

## Notes

- `CatalogDataRefreshService.cs` was not modified or removed — that is explicitly a later task
  (`remove-old-refresh-service-and-verify`) in this feature's task-context set, and
  `CatalogRepository`/`CatalogModule` wiring is likewise untouched here (also a later task,
  `wire-catalog-repository-and-module`). This step only adds the new standalone service.
- No deviation from the task-context's given code was needed (unlike the prior history-refresh
  task, which needed one added `using`) — the given file compiled and its test passed as written.

## PR Summary

Extracted ERP stock, eshop stock, in-transport, in-reserve/in-quarantine, ordered-quantity, and
manufactured/planned-inventory refresh logic from `CatalogDataRefreshService` into a new
standalone `CatalogStockRefreshService`, along with its own test file covering the one relevant
existing test case moved verbatim. `CatalogDataRefreshService` itself is left in place for now;
removing it and rewiring DI registrations are separate, later tasks in this feature.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogStockRefreshService.cs` — new service class (ERP/eshop stock, transport, reserve/quarantine, ordered, manufactured/planned refresh)
- `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogStockRefreshServiceTests.cs` — new test file (1 test moved from `CatalogDataRefreshServiceTests.cs`)

## Status
DONE
