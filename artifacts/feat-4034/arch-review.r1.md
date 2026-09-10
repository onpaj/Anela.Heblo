# Architecture Review: Remove redundant combined `Task.WhenAll` in `CalculateMonthlyStockChangeAsync`

## Skip Design: true

## Architectural Fit Assessment
This is a one-line dead-code deletion inside a single private method of `FinancialOverviewStockValueAdapter`, a Catalog-owned infrastructure adapter that implements FinancialOverview's `IStockValueService`. Verified against the current file: `CalculateMonthlyStockChangeAsync` (lines 119–162) creates `startStockTasks`/`endStockTasks`, awaits a combined `Task.WhenAll(startStockTasks.Concat(endStockTasks))` at line 141 whose result is discarded, then awaits `Task.WhenAll(startStockTasks)` / `Task.WhenAll(endStockTasks)` at lines 143–144 to get the actual `decimal[]` results used for the diff. The sibling method `GetStockValueChangeForPeriodAsync` (lines 78–117) already implements the identical start/end concurrent-fetch pattern correctly, with only the two result-capturing awaits (lines 103–104) and no combined call. Removing line 141 makes `CalculateMonthlyStockChangeAsync` structurally identical in shape to the already-correct sibling — this is a pure alignment-with-existing-pattern change, not a new pattern. No interfaces, contracts, DTOs, or module boundaries are touched.

## Proposed Architecture

### Component Overview
No component or relationship changes. Single method body edit within the existing adapter:

```
FinancialOverviewStockValueAdapter (unchanged public shape)
 ├─ GetStockValueChangesAsync (unchanged, calls CalculateMonthlyStockChangeAsync per month)
 ├─ GetStockValueChangeForPeriodAsync (unchanged — reference pattern)
 ├─ CalculateMonthlyStockChangeAsync (edited: delete dead line 141)
 └─ GetWarehouseStockValueAsync (unchanged)
```

### Key Design Decisions

#### Decision 1: Delete vs. keep the redundant `Task.WhenAll`
**Options considered:** (a) leave as-is; (b) delete the dead combined await, keeping the two result-capturing awaits.
**Chosen approach:** (b), matching the spec and the brief exactly.
**Rationale:** All six tasks (`GetWarehouseStockValueAsync` calls) are already started when the arrays are constructed (lines 127–139), before any `await` is reached — task creation, not awaiting, is what starts concurrent execution in this codebase's usage of `Task<T>`-returning async methods. The combined `Task.WhenAll` therefore adds no concurrency benefit and its result is never read; it is dead code whose only effect is to mislead a reader into thinking it does something the two subsequent awaits don't already do. Deleting it is strictly a clarity fix with zero behavioral or performance change, and brings the method in line with the already-correct sibling.

## Implementation Guidance

### Directory / Module Structure
No new files or directories. Edit only `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/FinancialOverviewStockValueAdapter.cs`, deleting line 141 (`await Task.WhenAll(startStockTasks.Concat(endStockTasks));`). Lines 143–144 are unchanged. Confirm `System.Linq`'s `Concat` extension (used only by the deleted line, per current read of the file) doesn't leave an unused `using` — the file has no explicit `using System.Linq;` (implicit usings are enabled per project convention), so no `using` cleanup is expected; verify with `dotnet build`/`dotnet format` per standard validation.

### Interfaces and Contracts
None affected. `IStockValueService`, `CalculateMonthlyStockChangeAsync`'s signature (private, unchanged), and `MonthlyStockChange`/`StockChangeByType` DTOs are untouched.

### Data Flow
Unchanged. `GetStockValueChangesAsync` still calls `CalculateMonthlyStockChangeAsync` once per month; that method still creates 6 concurrent warehouse-value tasks, awaits them via two `Task.WhenAll` calls, and computes `end - start` per warehouse type. Only the intermediate discarded await is removed.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| None identified — deleting an already-unused result introduces no behavior change | N/A | Existing test `StockValueServiceTests.GetStockValueChangesAsync_CalculatesCorrectStockValueChanges` exercises `GetStockValueChangesAsync` → `CalculateMonthlyStockChangeAsync` end-to-end (mocked `IErpStockClient`/`IProductPriceErpClient`) and asserts computed monthly values, giving a regression safety net for this exact code path; rerun it after the edit. |

## Specification Amendments
None. The spec (`artifacts/feat-4034/spec.r1.md`) is accurate as written and matches the current file content verified during this review: line 141 is the dead combined `Task.WhenAll`, lines 143–144 are the two result-capturing awaits to keep, and `GetStockValueChangeForPeriodAsync` (lines 78–117, with the reference pattern at lines 103–104) is confirmed as the correct-shape sibling. One clarification for the implementer: `StockValueServiceTests.cs` already covers `CalculateMonthlyStockChangeAsync` indirectly via `GetStockValueChangesAsync` — the spec's Out of Scope note that no new/modified tests are needed is confirmed correct; no test currently calls `CalculateMonthlyStockChangeAsync` or `GetStockValueChangeForPeriodAsync` directly, but the indirect coverage through `GetStockValueChangesAsync` is sufficient for this dead-code removal.

## Prerequisites
None. No migrations, config, or infrastructure changes required.
