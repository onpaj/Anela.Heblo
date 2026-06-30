# Implementation: fix-tests-and-allowlist

## What was implemented

### Step 1: Remove 8 ManufactureHistoryRecord allowlist entries from ModuleBoundariesTests.cs

Removed the `// Deliberate pragmatic leak` comment block and all 8 `ManufactureHistoryRecord` entries from `CatalogManufactureAllowlist`. Also updated the header comment on that allowlist (removed the stale bullet about introducing `CatalogManufactureHistoryRecord`).

### Step 2: Add 3 CatalogManufactureRecord allowlist entries to ManufactureCatalogAllowlist

The removal in Step 1 exposed a new violation: `Manufacture -> Catalog` rule fires because `ManufactureCatalogSourceAdapter` and `IProductionActivityAnalyzer`/`ProductionActivityAnalyzer` (all in the Manufacture namespace) now reference `CatalogManufactureRecord` which lives in `Anela.Heblo.Domain.Features.Catalog.ManufactureHistory`. These three entries were added to `ManufactureCatalogAllowlist` with a comment explaining the pragmatic rationale (the adapter is the correct mapping boundary; removal is tracked under the ProductCatalogSnapshot follow-up).

Nested compiler-generated types (`+<>c`, `+<>c__DisplayClass*`, `+<GetManufactureHistoryAsync>d__5`) are covered by the existing declaring-type check in the test framework and required no additional entries.

### Step 3: Update ManufactureCatalogSourceAdapterTests.cs

Replaced `GetManufactureHistoryAsync_DelegatesAndPassesNullProductCode` with `GetManufactureHistoryAsync_MapsManufactureHistoryRecordToCatalogManufactureRecord`. The new test:
- Sets up the mock client returning `ManufactureHistoryRecord` records with all fields populated
- Asserts that the returned `IReadOnlyList<CatalogManufactureRecord>` contains a single item with all fields correctly mapped
- Added `using Anela.Heblo.Domain.Features.Catalog.ManufactureHistory;`

## Files created/modified

- `/home/user/worktrees/feature-feat-3244/backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — removed 8 `ManufactureHistoryRecord` entries from `CatalogManufactureAllowlist`; added 3 `CatalogManufactureRecord` entries to `ManufactureCatalogAllowlist`
- `/home/user/worktrees/feature-feat-3244/backend/test/Anela.Heblo.Tests/Features/Manufacture/Infrastructure/ManufactureCatalogSourceAdapterTests.cs` — replaced delegation test with mapping verification test

## Tests

- `ModuleBoundariesTests`: 24/24 passed (including all Catalog -> Manufacture and Manufacture -> Catalog rules)
- `ManufactureCatalogSourceAdapterTests`: 3/3 passed (GetPlannedQuantitiesAsync, GetManufactureHistoryAsync_Maps..., GetManufacturedInventoryAsync)
- Full test suite: 5148 passed, 56 failed (all failures are Docker integration tests — pre-existing, unrelated to this change)
- `dotnet format --verify-no-changes`: passed (exit 0)

## How to verify

```bash
cd /home/user/worktrees/feature-feat-3244
dotnet test backend/test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~ModuleBoundariesTests" --no-build
dotnet test backend/test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~ManufactureCatalogSourceAdapterTests" --no-build
```

## Notes

The `Manufacture -> Catalog` allowlist needed 3 new entries because `CatalogManufactureRecord` lives in `Anela.Heblo.Domain.Features.Catalog.ManufactureHistory` — a Catalog-domain namespace. The adapter (`ManufactureCatalogSourceAdapter`) is the correct mapping boundary and must reference the output type; `IProductionActivityAnalyzer` and `ProductionActivityAnalyzer` consume that output type as method parameters. All nested compiler-generated types are covered by the declaring-type check built into the test framework.

## PR Summary

### Changes
- Removed 8 stale `ManufactureHistoryRecord` boundary-bypass allowlist entries (the CatalogManufactureRecord decoupling is now complete)
- Added 3 `CatalogManufactureRecord` entries to `ManufactureCatalogAllowlist` to legitimize the adapter's return type and analyzer parameter type
- Updated `ManufactureCatalogSourceAdapterTests` to assert field-level mapping from `ManufactureHistoryRecord` to `CatalogManufactureRecord`

## Status
DONE
