# Implementation: add-top-product-sorter

## What was implemented
Created `ITopProductSorter`/`TopProductSorter` as a verbatim move of the handler's current `ApplySorting` method, registered it as a Scoped service in `AnalyticsModule.cs`, and added a full-coverage test file. The handler is untouched by this task — its private `ApplySorting` method still exists and is still used; it will be removed in the next task (`wire-top-product-sorter-into-handler`).

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Analytics/Services/TopProductSorter.cs` — new `ITopProductSorter`/`TopProductSorter`.
- `backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs` — registered `ITopProductSorter, TopProductSorter` as Scoped.
- `backend/test/Anela.Heblo.Tests/Features/Analytics/TopProductSorterTests.cs` — new test file.

## Tests
- 13 sort keys × 2 directions (26 theory cases) covering groupkey/productcode, displayname/productname, totalmargin, m0-m2 amount/percentage, sellingprice, purchaseprice.
- Default (null/empty/whitespace sortBy), unrecognized-key fallback, and case-insensitivity (5 additional fact tests).
- Total 31 test cases.

## How to verify
`dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-restore` (0 errors), then `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~TopProductSorterTests"` (31/31 passed).

## Notes
Followed the task-context file's exact code verbatim. No deviations.

## PR Summary
Extracted the 13-branch sorting switch from `GetProductMarginSummaryHandler` into a new DI-registered `ITopProductSorter` service, with full test coverage across every sort key, both directions, and the default/fallback/case-insensitivity edge cases. This is step 3 of 4 in the SRP refactor from issue #3465.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Analytics/Services/TopProductSorter.cs` (new)
- `backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs`
- `backend/test/Anela.Heblo.Tests/Features/Analytics/TopProductSorterTests.cs` (new)

## Status
DONE
