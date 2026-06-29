# Implementation: add-test-fr3-unknown-sort-field-fallback

## What was implemented

Added `Handle_UnknownSortField_FallsBackToProductCodeAscending` test to `GetProductMarginsHandlerTests.cs`.

The test verifies FR-3: when `SortBy` is set to an unrecognised value (`"nonexistent"`), the handler silently falls back to sorting by `ProductCode` ascending. Three catalog items with codes `B001`, `A001`, `C001` (intentionally out of order) are fed in, and the response is asserted to return them in ascending order: `A001`, `B001`, `C001`.

## Files created/modified

- Modified: `backend/test/Anela.Heblo.Tests/Features/Catalog/GetProductMarginsHandlerTests.cs`
  - Added `Handle_UnknownSortField_FallsBackToProductCodeAscending` (41 lines) between the FR-2 test and the `BuildAggregate` helper.

## Tests

`dotnet test --filter "FullyQualifiedName~Handle_UnknownSortField_FallsBackToProductCodeAscending"`

Result: Passed (1/1).

## How to verify

```
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~Handle_UnknownSortField_FallsBackToProductCodeAscending"
```

## Notes

- The `BuildAggregate` helper defaults `type` to `ProductType.Product`, so the explicit `ProductType = ProductType.Product` in the request ensures all three items pass the product-type filter without needing to specify `type` on each `BuildAggregate` call.
- The access matrix generation step that runs as a post-build tool throws a JSON parse error (pre-existing issue unrelated to this change).

## PR Summary

Added FR-3 coverage: verifies that an unrecognised `SortBy` value silently falls back to `ProductCode` ascending sort.

## Status
DONE
