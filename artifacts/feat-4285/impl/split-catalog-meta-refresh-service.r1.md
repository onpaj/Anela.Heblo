# Implementation: split-catalog-meta-refresh-service

## What was implemented
Extracted a new `CatalogMetaRefreshService` class from the monolithic `CatalogDataRefreshService`, responsible for refreshing catalog metadata: product attributes, lots, eshop/ERP prices, and eshop URLs. This follows the exact same pattern as the previously extracted `CatalogHistoryRefreshService` and `CatalogStockRefreshService` in earlier tasks of this SRP-decomposition effort.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMetaRefreshService.cs` — new sealed class with 8 constructor-injected dependencies (`ICatalogAttributesClient`, `ILotsClient`, `IProductPriceEshopClient`, `IProductPriceErpClient`, `IProductEshopUrlClient`, `ICatalogResilienceService`, `CatalogCacheStore`, `ILogger<CatalogMetaRefreshService>`) and 5 refresh methods: `RefreshAttributesData`, `RefreshLotsData`, `RefreshEshopPricesData`, `RefreshErpPricesData`, `RefreshEshopUrlData`. Implemented verbatim per the task-context code snippet.

## Tests
None added. Per task-context Step 3 / spec FR-4, `CatalogDataRefreshServiceTests.cs` has no existing test cases for these five methods, so there is nothing to migrate 1:1. Adding new coverage is legitimate future work but out of scope for this SRP-only refactor.

## How to verify
```
cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```
Build succeeded with 0 errors (123 pre-existing warnings unrelated to this change).

## Notes
This task only creates the new standalone service class; it does not yet wire it into DI or remove the corresponding methods from `CatalogDataRefreshService`. That wiring/removal is handled by the later `wire-catalog-repository-and-module` and `remove-old-refresh-service-and-verify` tasks in this same task plan, consistent with how the two prior tasks (`split-catalog-history-refresh-service`, `split-catalog-stock-refresh-service`) were structured.

## PR Summary
Extracted `CatalogMetaRefreshService` from `CatalogDataRefreshService` as part of the ongoing SRP decomposition of the 22-constructor-parameter god class. The new service owns metadata refresh responsibilities (attributes, lots, eshop/ERP prices, eshop URLs) with 8 focused dependencies, mirroring the pattern already used for the history and stock refresh services extracted in prior tasks.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMetaRefreshService.cs` — new service class extracted from `CatalogDataRefreshService`

## Status
DONE
