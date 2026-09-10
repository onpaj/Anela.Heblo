# Code Review: remove-redundant-whenall

## Summary
The implementation deletes exactly the line the task specified — the redundant, discarded-result `await Task.WhenAll(startStockTasks.Concat(endStockTasks));` in `CalculateMonthlyStockChangeAsync` — and nothing else. Verified by direct diff inspection, a clean build, a targeted test run, and a scoped format check.

## Review Result: PASS

### task: remove-redundant-whenall
**Status:** PASS

Checks performed:
- **Spec compliance:** `git diff` on `FinancialOverviewStockValueAdapter.cs` shows exactly the specified statement plus its trailing blank line removed; the resulting method body matches the "after" snippet in the task-context file verbatim. The two remaining `Task.WhenAll` awaits, the method signature, `GetStockValueChangeForPeriodAsync`, `GetWarehouseStockValueAsync`, and all `using` directives are untouched.
- **Correctness:** All six `Task<decimal>`-returning calls in `startStockTasks`/`endStockTasks` are started at array-initializer time (before any `await`), so the deleted combined await was provably a no-op — its result was never read, and the two remaining awaits already await the identical task references. No behavior change.
- **Architecture adherence:** The method now structurally matches the sibling `GetStockValueChangeForPeriodAsync` in the same file (confirmed by direct read), which already uses the two-individual-awaits pattern with no combined await.
- **Completeness / verification:** `dotnet build Anela.Heblo.sln` succeeds with 0 errors. `dotnet test` against the rebuilt `Anela.Heblo.Tests.dll` filtered to `StockValueServiceTests` reports `Passed! - Failed: 0, Passed: 4, Skipped: 0, Total: 4`. `dotnet format Anela.Heblo.sln --verify-no-changes --include <the changed file>` exits 0 with no output (no formatting issues).
- **No unused-using regression:** No explicit `using System.Linq;` existed in the file (the project relies on implicit usings), so removing the only `.Concat(` call in the file introduces no unused-using warning — confirmed by grep.

No issues found.

## Docs to Update
(none — internal, behavior-preserving dead-code removal with no public API, config, or operational surface change)

## Overall Notes
None. This is a minimal, surgical, verified change exactly matching the task-context specification.

**Status:** PASS
