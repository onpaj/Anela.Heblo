# Implementation: extract-sortby-helper-and-refactor-applysorting

## What was implemented
Extracted the repeated `sortDescending ? OrderByDescending(...) : OrderBy(...)` ternary
out of `GetProductMarginsHandler.ApplySorting` into a single private static generic
helper, `SortBy<TKey>`, and rewrote every arm of the `switch` (plus the pre-switch
null/whitespace guard and the default arm) to call it. Every selector lambda was
copied verbatim from the arm it replaces — no behavior change.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductMargins/GetProductMarginsHandler.cs` — added `private static IOrderedEnumerable<CatalogAggregate> SortBy<TKey>(IEnumerable<CatalogAggregate> items, Func<CatalogAggregate, TKey> key, bool desc)` and rewrote `ApplySorting` to call it for the default sort, all 13 named `switch` arms (`productcode`, `productname`, `pricewithoutvat`, `purchaseprice`, `manufacturedifficulty`, `m0amount`..`m3amount`, `m0percentage`..`m3percentage`), and the fallback `_` arm — exactly as specified in the task context.

## Tests
No new tests were required by this task — behavior preservation is verified by the
existing `GetProductMarginsHandlerTests` suite (5 pre-existing tests + the 2
characterization tests added in the prior `add-characterization-tests` task).

## How to verify
```bash
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetProductMarginsHandlerTests"
dotnet format Anela.Heblo.sln --verify-no-changes
```
Results:
- `dotnet build` — succeeded, 0 errors (248 pre-existing warnings, none new, none in the touched file).
- `GetProductMarginsHandlerTests` — **7/7 passed**, including
  `Handle_SortByM0AmountDescending_OrdersByCalculatedM0Amount` and
  `Handle_SortByM0PercentageAscending_OrdersByCalculatedM0Percentage`.
- `dotnet format --verify-no-changes` — reports whitespace issues only in
  `backend/test/Anela.Heblo.Tests/Features/MarketingPerformance/*HandlerTests.cs`,
  confirmed pre-existing (from PR #4235, merged before this branch) and unrelated to
  the touched file; `GetProductMarginsHandler.cs` has no formatting complaints.
- Full solution `dotnet test Anela.Heblo.sln` — 7799 passed, 111 failed, 4 skipped.
  Every failure is a Docker/Testcontainers-dependent integration test (Postgres
  `Testcontainers` container startup, or Flexi integration fixtures) that fails in
  this sandboxed environment because no Docker daemon is available — none touch
  Catalog/GetProductMargins, and none are new relative to what this environment
  produces on any run that doesn't have Docker (this handler change is a pure
  in-memory LINQ refactor with no DB/container surface).

## Notes
No deviations from the task context — the replacement code block was pasted exactly
as given, and every selector lambda was diffed against the pre-refactor arm it
replaces before committing (all matched verbatim).

## PR Summary
Extracted a `SortBy<TKey>` helper in `GetProductMarginsHandler.ApplySorting` to
deduplicate the `sortDescending ? OrderByDescending : OrderBy` ternary that was
repeated 15 times (13 named `switch` arms + the default arm + the pre-switch
null-guard). Purely a readability refactor — no behavior change, verified by the
existing and characterization tests all passing unchanged.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductMargins/GetProductMarginsHandler.cs` — added `SortBy<TKey>` helper; every `ApplySorting` arm now calls it instead of repeating the ternary inline.

## Status
DONE
