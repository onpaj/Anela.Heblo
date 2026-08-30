# Implementation: catalog-eshop-stock-source-adapter

## What was implemented
A Catalog-side adapter, `DataQualityEshopStockSourceAdapter`, that implements the DataQuality-owned
`IDqtEshopStockSource` contract by wrapping the Catalog domain's `IEshopStockClient` and projecting
`EshopStock` (Catalog domain type) into `DqtEshopStockItem` (DataQuality contract DTO). This keeps
`Anela.Heblo.Application.Features.DataQuality` free of direct references to Catalog domain types,
resolving the module-boundary violation flagged for `ProductPairingDqtComparer` (issue #3967). The
adapter is intentionally `internal sealed` and is not yet wired into DI — that is a separate later
task (`catalog-module-di-registration`).

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/DataQualityEshopStockSourceAdapter.cs` — new `internal sealed class DataQualityEshopStockSourceAdapter : IDqtEshopStockSource`, constructor-injects `IEshopStockClient`, and projects `Code`/`PairCode`/`Name` from `EshopStock` to `DqtEshopStockItem` in `ListAsync`.
- `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/DataQualityEshopStockSourceAdapterTests.cs` — new test file with 3 tests covering the projection.

## Tests
`DataQualityEshopStockSourceAdapterTests` (3 tests, using Moq + FluentAssertions):
- `ListAsync_ProjectsCodePairCodeAndName` — single item is correctly mapped field-by-field.
- `ListAsync_WhenInnerReturnsEmpty_ReturnsEmptyList` — empty inner result yields empty list.
- `ListAsync_ProjectsMultipleProductsInOrder` — multiple items are projected and preserve order.

## How to verify
```
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DataQualityEshopStockSourceAdapterTests"
```
Expected: `Passed! - Failed: 0, Passed: 3, Skipped: 0, Total: 3`.

## Notes
- TDD red step confirmed: before the implementation file existed, the test project failed to build with
  `error CS0246: The type or namespace name 'DataQualityEshopStockSourceAdapter' could not be found`.
- No deviations from the task spec — test and implementation files match the verbatim content given.
- Per hard constraints, no DI/module registration file was touched, and no other files were modified
  besides the two named files (a pre-existing unrelated change to `artifacts/feat-3967/state.json` was
  left unstaged/uncommitted).

## Status
DONE
