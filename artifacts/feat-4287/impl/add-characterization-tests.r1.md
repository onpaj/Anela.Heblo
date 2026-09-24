# Implementation: add-characterization-tests

## What was implemented
Added two characterization tests to `GetProductMarginsHandlerTests` that lock in the
current (pre-refactor) sorting behavior for the `m0amount` and `m0percentage` sort
fields in `GetProductMarginsHandler.ApplySorting`, plus a small helper to build a
`CatalogAggregate` with a specific pre-calculated M0 margin amount/percentage. These
tests must keep passing unchanged after the upcoming `SortBy<TKey>` extraction
refactor (task `extract-sortby-helper-and-refactor-applysorting`), which is what
proves the refactor is behavior-preserving.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/Catalog/GetProductMarginsHandlerTests.cs` — added `Handle_SortByM0AmountDescending_OrdersByCalculatedM0Amount`, `Handle_SortByM0PercentageAscending_OrdersByCalculatedM0Percentage`, and the `BuildAggregateWithM0Margin` helper, exactly as specified in the task context.

## Tests
- `Handle_SortByM0AmountDescending_OrdersByCalculatedM0Amount` — sorts three products by `m0amount` descending and asserts the order `HIGH001, MID001, LOW001`.
- `Handle_SortByM0PercentageAscending_OrdersByCalculatedM0Percentage` — sorts the same three products by `m0percentage` ascending and asserts the order `LOW001, MID001, HIGH001`.

## How to verify
```bash
dotnet build
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetProductMarginsHandlerTests"
```
Result: build succeeds with 0 errors (pre-existing warnings only, none new); all 7 tests in `GetProductMarginsHandlerTests` (5 pre-existing + 2 new) pass against the current, pre-refactor `ApplySorting` implementation.

## Notes
No deviations from the task context — code was copied verbatim from the task-context file. `MarginLevel`'s constructor signature (`percentage, amount, costTotal, costLevel`) and `MarginData.M0`'s `init` accessor were confirmed against the actual domain types before use; both match what the task context assumed.

## PR Summary
Added characterization tests for `m0amount`/`m0percentage` sorting in `GetProductMarginsHandler` ahead of the `SortBy<TKey>` extraction refactor for issue #4287, so the refactor's behavior-preservation claim is verifiable.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/Catalog/GetProductMarginsHandlerTests.cs` — two new characterization tests + a supporting `BuildAggregateWithM0Margin` helper.

## Status
DONE
