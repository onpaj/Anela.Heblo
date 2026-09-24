# Implementation: replace-inline-construction-in-get-catalog-detail-handler

## What was implemented
Replaced the four inline `new MarginLevelDto { ... }` object initializers (M0-M3) inside `GetCatalogDetailHandler.GetMarginHistoryFromMargins` with calls to the existing `MarginLevelDto.FromDomain(...)` factory method, exactly as specified in the task context. `FromDomain` maps `Percentage`, `Amount`, `CostLevel`, `CostTotal` field-for-field, identically to what was inlined before, so this is a pure, behavior-preserving refactor. This mirrors the same replacement already done and reviewed in the sibling task `replace-inline-construction-in-get-product-margins-handler`.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetCatalogDetail/GetCatalogDetailHandler.cs` — replaced 4 inline `MarginLevelDto` initializers (M0, M1, M2, M3) with `MarginLevelDto.FromDomain(...)` calls in `GetMarginHistoryFromMargins`.

## Tests
- `backend/test/Anela.Heblo.Tests/Features/Catalog/GetCatalogDetailHandlerTests.cs` (existing, not modified)
- `backend/test/Anela.Heblo.Tests/Features/Catalog/GetCatalogDetailHandlerFullHistoryTests.cs` (existing, not modified)

Both suites run together (`--filter "FullyQualifiedName~GetCatalogDetailHandlerTests|FullyQualifiedName~GetCatalogDetailHandlerFullHistoryTests"`) after the edit: **13/13 passed, 0 failed** (`dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`).

## How to verify
1. `grep -n "new MarginLevelDto" backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetCatalogDetail/GetCatalogDetailHandler.cs` — 0 matches (confirmed).
2. `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetCatalogDetailHandlerTests|FullyQualifiedName~GetCatalogDetailHandlerFullHistoryTests"` — 13 passed, 0 failed.

## Notes
The sandbox environment showed transient MSBuild deadlocks (multiple full-solution `dotnet test` runs hung indefinitely inside the solution's `GenerateAccessMatrix` pre-build target, a nested `dotnet run` invocation, most likely due to shared MSBuild/VBCSCompiler server contention with a concurrent build running in a sibling worktree on the same host). This was infrastructure/environment contention, not an application issue — confirmed via `/proc/<pid>/stack` showing all threads parked in `futex_do_wait` with zero CPU growth for minutes. Retrying with `-nodeReuse:false -p:UseSharedCompilation=false MSBUILDDISABLENODEREUSE=1` avoided the contention and the run completed normally with a clean pass. No source changes were made to work around this; it only affected local verification, not the change itself.

## PR Summary
Continued the `MarginLevelDto` construction-duplication cleanup (feat-4286) by applying the same `FromDomain(...)` factory replacement, already used in `GetProductMarginsHandler`, to `GetCatalogDetailHandler.GetMarginHistoryFromMargins`. Pure refactor, no behavior change; all affected tests pass.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetCatalogDetail/GetCatalogDetailHandler.cs` — M0-M3 `MarginLevelDto` construction now goes through `MarginLevelDto.FromDomain(...)`.

## Status
DONE
