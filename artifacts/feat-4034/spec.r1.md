# Specification: Remove redundant combined `Task.WhenAll` in `CalculateMonthlyStockChangeAsync`

## Summary
`FinancialOverviewStockValueAdapter.CalculateMonthlyStockChangeAsync` contains a dead-code line: a combined `Task.WhenAll(startStockTasks.Concat(endStockTasks))` await whose result is discarded, immediately followed by the two `Task.WhenAll` awaits that actually capture the results. This is a one-line dead-code removal with no behavior or performance change. The fix aligns the method with the correct, already-existing pattern used in the sibling method `GetStockValueChangeForPeriodAsync` in the same file.

## Background
`FinancialOverviewStockValueAdapter` computes monthly stock value changes by querying stock values for three warehouses (Materials, SemiProducts, Products) at both the start and end of a period, concurrently, then diffing end minus start. Two methods implement this pattern: `GetStockValueChangeForPeriodAsync` (arbitrary period) and `CalculateMonthlyStockChangeAsync` (calendar month), the latter invoked once per month by `GetStockValueChangesAsync`.

In `CalculateMonthlyStockChangeAsync`, line 141 awaits `Task.WhenAll` over the concatenation of all six start/end tasks but never uses its result. Lines 143–144 then await `Task.WhenAll` on `startStockTasks` and `endStockTasks` separately to obtain the `decimal[]` results actually used to compute the diff. Because all six tasks are created (and thus started) before any of these awaits run, the first `Task.WhenAll` adds no concurrency benefit — it is pure overhead and a readability hazard, since it implies (incorrectly) that the subsequent two awaits are needed for some reason other than result extraction. `GetStockValueChangeForPeriodAsync` (lines 103–104) already implements the same start/end concurrent-fetch pattern without this redundant call, and is the reference for correct shape.

## Functional Requirements

### FR-1: Remove the redundant combined `Task.WhenAll` await
Delete line 141 (`await Task.WhenAll(startStockTasks.Concat(endStockTasks));`) from `CalculateMonthlyStockChangeAsync` in `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/FinancialOverviewStockValueAdapter.cs`. The two subsequent awaits (`var startValues = await Task.WhenAll(startStockTasks);` and `var endValues = await Task.WhenAll(endStockTasks);`) are kept unchanged and continue to drive concurrent execution of all six warehouse queries, since `startStockTasks` and `endStockTasks` are already-started task arrays created earlier in the method body.

**Acceptance criteria:**
- Line 141 (`await Task.WhenAll(startStockTasks.Concat(endStockTasks));`) no longer exists in the file.
- The two remaining `Task.WhenAll` awaits (previously lines 143–144) are unchanged in behavior and continue to populate `startValues` and `endValues` used for the `Materials`/`SemiProducts`/`Products` diff computation.
- `CalculateMonthlyStockChangeAsync` now mirrors the shape of `GetStockValueChangeForPeriodAsync` (create all tasks, then two `Task.WhenAll` awaits, no intermediate combined await).
- No other lines in the file are modified.
- No `using` directives become unused as a result of this change (`System.Linq`'s `Concat` may still be used elsewhere in the file — verify at edit time; if not, leave `using` statements as-is unless the compiler/analyzer flags an actually-unused directive).

## Non-Functional Requirements

### NFR-1: Performance
No performance change is expected or targeted. All six warehouse-value tasks (`GetWarehouseStockValueAsync` calls) are already started concurrently before the first await is reached, regardless of whether the redundant combined `Task.WhenAll` is present. Removing it eliminates one redundant await/continuation but this has no user-observable effect on latency or throughput.

### NFR-2: Security
N/A — no change to authentication, authorization, or data handling. No sensitive data is touched by this change.

## Data Model
N/A — no change to `MonthlyStockChange`, `StockChangeByType`, or any other domain/DTO type.

## API / Interface Design
N/A — no change to `IStockValueService`, method signatures, or any public/internal contract. `CalculateMonthlyStockChangeAsync` remains a private method with the same signature and return type.

## Dependencies
None. Self-contained change within a single file; no new libraries, services, or feature flags involved.

## Out of Scope
- Any change to `GetStockValueChangeForPeriodAsync`, `GetStockValueChangesAsync`, or `GetWarehouseStockValueAsync`.
- Any behavioral, performance, or concurrency change to stock value calculation.
- Any refactor beyond deleting the single redundant line (e.g., extracting a shared helper for the start/end task-pair pattern used in both methods) — not requested by the brief and would exceed the "surgical change" scope.
- Adding or modifying tests specifically for this line, since it was dead code with no observable behavior to test; existing tests covering `CalculateMonthlyStockChangeAsync`/`GetStockValueChangesAsync` output should continue to pass unchanged and serve as sufficient regression coverage.

## Open Questions
None. The change is a single-line dead-code deletion, verified against the current file content (line 141 of `FinancialOverviewStockValueAdapter.cs`, matching the brief), with a clear correct-pattern reference already present in the same file.

## Status: COMPLETE
