# Implementation: catalog-erp-stock-source-adapter

## What was implemented
A Catalog-side adapter, `DataQualityErpStockSourceAdapter`, that implements the DataQuality-owned
`IDqtErpStockSource` contract by wrapping the Catalog domain's `IErpStockClient` and projecting
`ErpStock` (Catalog domain type) into `DqtErpStockItem` (DataQuality contract DTO). This is the
ERP-side counterpart to `DataQualityEshopStockSourceAdapter` (completed in a prior task) and keeps
`Anela.Heblo.Application.Features.DataQuality` free of direct references to Catalog domain types,
per the module-boundary fix for `ProductPairingDqtComparer` (issue #3967). `IsSellable` is derived
from `ErpStock.ProductTypeId`, true only for `ProductType.Goods` (1) and `ProductType.Product` (8);
all other type ids (including `null`) map to `false`. The adapter is intentionally `internal sealed`
and is not yet wired into DI — that is a separate later task (`catalog-module-di-registration`).

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/DataQualityErpStockSourceAdapter.cs` — new `internal sealed class DataQualityErpStockSourceAdapter : IDqtErpStockSource`, constructor-injects `IErpStockClient`, and projects `ProductCode`/`ProductName`/`IsSellable` from `ErpStock` to `DqtErpStockItem` in `ListAsync`.
- `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/DataQualityErpStockSourceAdapterTests.cs` — new test file with 9 tests (via `[Theory]`/`[InlineData]`) covering the projection and the `IsSellable` mapping for every `ProductType` value plus `null`.

## Tests
`DataQualityErpStockSourceAdapterTests` (9 tests total, using Moq + FluentAssertions):
- `ListAsync_ProjectsProductCodeAndProductName` — single item's code/name are correctly mapped.
- `ListAsync_MapsIsSellable_FromProductTypeId` (6 inline cases) — `IsSellable` is `true` only for `ProductTypeId` 1 (Goods) and 8 (Product); `false` for Material (3), SemiProduct (7), Set (99), and UNDEFINED (0).
- `ListAsync_WhenProductTypeIdIsNull_IsSellableIsFalse` — `null` `ProductTypeId` maps to `IsSellable == false`.
- `ListAsync_WhenInnerReturnsEmpty_ReturnsEmptyList` — empty inner result yields empty list.

## How to verify
```
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DataQualityErpStockSourceAdapterTests"
```
Result: `Test Run Successful. Total tests: 9, Passed: 9`.

## Notes
- Verified against the task-context file verbatim: test file, implementation file, and the
  `ProductType` enum values (`Product = 8, Goods = 1, Material = 3, SemiProduct = 7, Set = 99,
  UNDEFINED = 0`) all matched exactly, so no deviations were needed.
- `dotnet format Anela.Heblo.sln --include <the two new files>` was run and made no changes — the
  files were already compliant with the repo's formatting rules.
- `DqtErpStockItem` is a plain class (not a record), consistent with the project's DTO rule.
- Per hard constraints, no DI/module registration file was touched, and no other files were
  modified besides the two named files (`artifacts/feat-3967/state.json` checkpoint updates are
  handled separately by the orchestrator, not by this developer step).

## PR Summary
Added `DataQualityErpStockSourceAdapter`, the ERP-side Catalog adapter implementing the
DataQuality-owned `IDqtErpStockSource` contract. It wraps `IErpStockClient` and projects
`ErpStock` into `DqtErpStockItem`, deriving `IsSellable` from `ProductTypeId` (true only for
Goods/Product). This mirrors the eshop-side adapter completed earlier and is the second of the two
source adapters needed before `ProductPairingDqtComparer` can be rewired away from direct Catalog
domain access.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/DataQualityErpStockSourceAdapter.cs` — new adapter class
- `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/DataQualityErpStockSourceAdapterTests.cs` — 9 unit tests

## Status
DONE
