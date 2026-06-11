Working tree is clean. The implementation is complete.

---

# Implementation: Remove unused `ILotsClient` from `GetCatalogDetailHandler`

## What was implemented

Pure compile-time refactor: removed the dead `ILotsClient` constructor dependency and `_lotsClient` field from `GetCatalogDetailHandler`, plus the corresponding `_lotsClientMock` from both handler test fixtures. The handler's runtime behavior is unchanged — lots continue to be sourced from `CatalogAggregate.Stock.Lots` via the cache pipeline.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetCatalogDetail/GetCatalogDetailHandler.cs` — removed `using ...Catalog.Lots;`, `_lotsClient` field, `ILotsClient lotsClient` constructor parameter and assignment
- `backend/test/Anela.Heblo.Tests/Features/Catalog/GetCatalogDetailHandlerTests.cs` — removed `using ...Catalog.Lots;`, `_lotsClientMock` field, mock instantiation, and constructor argument
- `backend/test/Anela.Heblo.Tests/Features/Catalog/GetCatalogDetailHandlerFullHistoryTests.cs` — same removals as above

## Tests

All existing tests in both handler fixtures pass (12 test executions). The broader Catalog slice (482 tests) also passes. `CatalogRepositoryTests` and `CatalogRepositoryCacheOptimizationTests` — which have their own `_lotsClientMock` for `CatalogRepository` — were not touched and continue to pass.

## How to verify

```bash
dotnet build backend/Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetCatalogDetailHandler" --no-build
```

Both must succeed with zero errors/failures.

## Notes

- The commit is atomic (all three files in one commit). The solution is intentionally non-compiling between individual file edits — the lockstep approach is by design for a signature-coupled refactor.
- `ILotsClient` interface, DI registration (`FlexiAdapterServiceCollectionExtensions`), and legitimate consumers (`CatalogDataRefreshService`, `CatalogRepository`) are untouched.
- A verification subagent modified `GetIssuedInvoiceDetailHandlerTests.cs` (adding a pre-existing missing using) but did not stage it. That change has been reverted — it is out of scope for this task.
- `dotnet format` was applied to the three touched files; the formatter produced no meaningful changes beyond the removals already made.

## PR Summary

Removes the unused `ILotsClient` constructor dependency from `GetCatalogDetailHandler`. The handler never calls any method on the client — lots are populated from `CatalogAggregate.Stock.Lots` sourced via the cache pipeline. The unused injection added noise to the constructor signature and obscured the handler's actual data flow.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetCatalogDetail/GetCatalogDetailHandler.cs` — removed `ILotsClient` parameter, `_lotsClient` field, assignment, and orphaned `using` directive
- `backend/test/Anela.Heblo.Tests/Features/Catalog/GetCatalogDetailHandlerTests.cs` — removed `_lotsClientMock` field, instantiation, and constructor argument
- `backend/test/Anela.Heblo.Tests/Features/Catalog/GetCatalogDetailHandlerFullHistoryTests.cs` — same removals as above

## Status
DONE