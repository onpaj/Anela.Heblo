# Implementation: wire-catalog-repository-and-module

## What was implemented

Repointed every consumer of `CatalogDataRefreshService` at the four new refresh services
(`CatalogHistoryRefreshService`, `CatalogStockRefreshService`, `CatalogMetaRefreshService`,
`CatalogReferenceRefreshService`) that earlier tasks in this feature extracted. Both the old
and new classes still coexist after this task — `CatalogDataRefreshService` itself is untouched
here and is removed in the final task (`remove-old-refresh-service-and-verify`).

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogRepository.cs` — constructor and
  field list replaced (`_refreshService` → `_historyRefreshService`, `_stockRefreshService`,
  `_metaRefreshService`, `_referenceRefreshService`); each `Refresh*Data` delegate method
  repointed at the correct new service per the task context's mapping.
- `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs` — DI registration
  swapped from `services.AddTransient<CatalogDataRefreshService>()` to four
  `AddTransient<Catalog*RefreshService>()` registrations. `RegisterBackgroundRefreshTasks` was
  not touched (it only ever referenced `ICatalogRepository`).
- `backend/test/Anela.Heblo.Tests/Domain/Catalog/CatalogRepositoryTests.cs` — field/construction
  block replaced with the four new services, each built from the file's existing mocks per the
  task context's Step 4 mapping.
- `backend/test/Anela.Heblo.Tests/Features/Catalog/CatalogRepositoryCacheOptimizationTests.cs` —
  same transformation. `_refreshServiceLoggerMock` was retyped from
  `Mock<ILogger<CatalogDataRefreshService>>` to `Mock<ILogger<CatalogHistoryRefreshService>>`
  because the one test that asserts against it
  (`RefreshSalesData_WhenResilienceServiceThrows_RetainsStaleCacheAndLogsWarning`) exercises
  `RefreshSalesData`, which the new delegate mapping routes to `_historyRefreshService` — and
  `CatalogHistoryRefreshService.cs` is what actually logs the "retaining stale cache" warning
  for that path now.
- `backend/test/Anela.Heblo.Tests/Features/Catalog/CatalogRepositoryStaleDataAndChangesPendingTests.cs`
  — same transformation using local variables (this file has no refresh-service field).
- `backend/test/Anela.Heblo.Tests/Features/Catalog/MarginCostWindowAlignmentTests.cs` — same
  transformation using `Mock.Of<T>()` (this file's own convention).

## Tests

No new test files — existing tests in the four files above continue to cover `CatalogRepository`
behavior, now constructed against the four new services instead of the single old one.

## How to verify

```bash
cd backend
dotnet build Anela.Heblo.sln            # 0 errors
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~CatalogRepositoryTests|FullyQualifiedName~CatalogRepositoryCacheOptimizationTests|FullyQualifiedName~CatalogRepositoryStaleDataAndChangesPendingTests|FullyQualifiedName~MarginCostWindowAlignmentTests"
```

Ran both: build succeeded (0 errors, pre-existing unrelated warnings only), and
`Passed! - Failed: 0, Passed: 38, Skipped: 0, Total: 38`.

Also ran the full Catalog-tagged slice of the suite
(`--filter "FullyQualifiedName~Catalog"`, 1035 tests): `Failed: 4, Passed: 1031`. The 4 failures
are `GetStockUpOperationsSummaryIntegrationTests` cases that fail with
`System.ArgumentException : Docker is either not running or misconfigured` — a pre-existing
Testcontainers/Docker-availability issue in this sandbox, unrelated to this change (they don't
touch `CatalogRepository` or the refresh services at all).

Did not run the full solution-wide `dotnet test Anela.Heblo.sln` to completion: it also spins up
`Anela.Heblo.Adapters.Flexi.Tests` integration tests that hit a live Flexi API / real database
and hang indefinitely in this sandboxed environment with no network egress to those systems —
this is a pre-existing environmental limitation, not something introduced by this task.

## Notes

- No deviation from the task-context's given code was needed for `CatalogRepository.cs`,
  `CatalogModule.cs`, or `CatalogRepositoryTests.cs` — applied verbatim.
- The three "apply the identical transformation" files (Step 5) required reading each file's
  actual mock/field layout since the task context didn't give verbatim before/after blocks for
  them, per its own instructions. All three followed the same constructor-parameter mapping as
  Step 4 with no surprises, except the `_refreshServiceLoggerMock` retype noted above in
  `CatalogRepositoryCacheOptimizationTests.cs`, which was needed for that file's own log
  assertion to keep testing the right thing after the delegate split.

## PR Summary

Repoints every consumer of `CatalogDataRefreshService` at the four new focused refresh services
extracted in earlier tasks of this feature: `CatalogRepository`'s constructor and its
`Refresh*Data` delegate methods, DI registration in `CatalogModule`, and the four test files that
construct `CatalogRepository` directly. `CatalogDataRefreshService` itself is untouched — both
old and new classes coexist until the final task removes the old one.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogRepository.cs` — repointed at the four new services
- `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs` — DI registration for the four new services
- `backend/test/Anela.Heblo.Tests/Domain/Catalog/CatalogRepositoryTests.cs` — updated construction
- `backend/test/Anela.Heblo.Tests/Features/Catalog/CatalogRepositoryCacheOptimizationTests.cs` — updated construction, retyped logger mock
- `backend/test/Anela.Heblo.Tests/Features/Catalog/CatalogRepositoryStaleDataAndChangesPendingTests.cs` — updated construction
- `backend/test/Anela.Heblo.Tests/Features/Catalog/MarginCostWindowAlignmentTests.cs` — updated construction

## Status
DONE
