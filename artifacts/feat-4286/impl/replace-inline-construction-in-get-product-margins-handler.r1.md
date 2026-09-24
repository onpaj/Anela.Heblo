# Implementation: replace-inline-construction-in-get-product-margins-handler

## What was implemented
Replaced the inline `new MarginLevelDto { ... }` object-initializer blocks in
`GetProductMarginsHandler.MapToMarginDto` with calls to the `MarginLevelDto.FromDomain`
factory method (added in the prior task `add-marginleveldto-factory`), for both the
M0-M3 averages block and the M0-M3 blocks inside the `MonthlyHistory` projection.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductMargins/GetProductMarginsHandler.cs` — replaced 8 inline `new MarginLevelDto { ... }` constructions (4 in the averages block, 4 in the `MonthlyHistory` `Select` projection) with `MarginLevelDto.FromDomain(...)` calls. No other logic in the file was touched.

## Tests
No new tests were required by the task context — this is a pure refactor with no behavior change. Ran the existing `GetProductMarginsHandlerTests` suite (`backend/test/Anela.Heblo.Tests/Features/Catalog/GetProductMarginsHandlerTests.cs`) before and after the edit.

## How to verify
1. `grep -n "new MarginLevelDto" backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductMargins/GetProductMarginsHandler.cs` returns no matches.
2. `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetProductMarginsHandlerTests"` — Passed! Failed: 0, Passed: 5, Skipped: 0, Total: 5 (identical to the pre-edit baseline run).

## Notes
Followed the task-context's step-by-step diff exactly; no deviations. `MarginLevelDto.FromDomain` (from the completed `add-marginleveldto-factory` task) maps `Percentage`, `Amount`, `CostLevel`, and `CostTotal` from the domain `MarginLevel`, matching the fields previously set inline field-for-field.

## PR Summary
Continued the MarginLevelDto construction-duplication cleanup from arch-review #4286: `GetProductMarginsHandler` now builds all eight `MarginLevelDto` instances (the M0-M3 averages and the M0-M3 entries in each `MonthlyHistory` row) via the new `MarginLevelDto.FromDomain` factory instead of repeating the same four-field object initializer inline. Behavior is unchanged; the existing handler test suite passes identically before and after.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductMargins/GetProductMarginsHandler.cs` — replaced inline `new MarginLevelDto { ... }` blocks with `MarginLevelDto.FromDomain(...)` calls

## Status
DONE
