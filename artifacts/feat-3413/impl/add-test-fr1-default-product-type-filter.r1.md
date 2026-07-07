# Implementation: add-test-fr1-default-product-type-filter

## What was implemented

Added a new xUnit test `Handle_NullProductType_ReturnsOnlyProductAndGoods` to `GetProductMarginsHandlerTests`. The test verifies FR-1: when `ProductType` is null in the request, `ApplyFilters` returns only items with `ProductType.Product` and `ProductType.Goods`, filtering out `SemiProduct` and `Material` items.

## Files created/modified

- `backend/test/Anela.Heblo.Tests/Features/Catalog/GetProductMarginsHandlerTests.cs` — added one new `[Fact]` test method (38 lines) before the `BuildAggregate` helper, using the existing `BuildAggregate` overload that accepts a `type` parameter

## Tests

```
Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1, Duration: < 1 ms
```

Test `Handle_NullProductType_ReturnsOnlyProductAndGoods` passes.

## How to verify

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~Handle_NullProductType_ReturnsOnlyProductAndGoods"
```

Expected: 1 test passed, `TotalCount == 2`, items are `PROD001` and `GOOD001`.

## Notes

- The `AccessMatrixGen` post-build tool throws a `JsonException` during the full `dotnet test` invocation (pre-existing issue unrelated to this change). Running `dotnet test --no-build` after a targeted `dotnet build` of the test project avoids this and confirms the test passes cleanly.
- No deviations from the task spec; the test body matches the spec exactly.

## PR Summary

Adds a unit test covering the FR-1 requirement that a null `ProductType` filter in `GetProductMarginsRequest` defaults to returning only `Product` and `Goods` catalog items.

## Status
DONE
