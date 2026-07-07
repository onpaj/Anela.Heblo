# Architecture Review: Deduplicate `FinancialSummaryDto` construction in `FinancialAnalysisService`

## Skip Design: true

## Architectural Fit Assessment

This is a private-method refactor entirely internal to `FinancialAnalysisService` (`backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs`), a single-implementation service registered behind `IFinancialAnalysisService` in `FinancialOverviewModule.cs`. It touches none of the module boundaries the project's `development_guidelines.md` cares about:

- No change to `contracts/`-style DTOs (`FinancialSummaryDto`, `StockSummaryDto`, `MonthlyFinancialDataDto` — all in `Model/`, all classes already, per the "DTOs are never records" rule) — shapes are untouched, only construction is consolidated.
- No change to `IFinancialAnalysisService`, `GetFinancialOverviewRequest`/`Response`, or `GetFinancialOverviewHandler` (the MediatR entry point) — module contract surface is identical.
- No new cross-module dependency, no persistence change, no DI change.

The finding is accurate: `GetHybridWithCurrentMonthAsync` (L317-330), `GetCachedFinancialOverview` (L375-388), and `GetFinancialOverviewRealTimeAsync` (L477-497) each inline an identical six-field `FinancialSummaryDto` block, and two `CreateStockSummary` overloads (L504-518 operating on `List<MonthlyFinancialDataDto>`, L520-536 operating on `List<MonthlyFinancialData>` + `List<MonthlyStockChange>`) compute the same `StockSummaryDto` shape from different inputs. This is textbook Extract Method / unify-overload territory — no architectural pattern needs to change, only the internal factoring of one file.

This aligns with existing conventions in the file: private `static` helpers already exist for shared computation (`CalculatePeriodTotals`, `MapToDto`), so adding a third private static helper (`BuildSummary`) is consistent with, not a departure from, the file's own style.

## Proposed Architecture

### Component Overview

No component-level change. Internal call graph inside `FinancialAnalysisService` goes from:

```
GetHybridWithCurrentMonthAsync ──┐
GetCachedFinancialOverview ──────┼──► inline `new FinancialSummaryDto { ... }` (×3, duplicated)
GetFinancialOverviewRealTimeAsync┘         │
                                            ├──► CreateStockSummary(List<MonthlyFinancialDataDto>)   [used by 2 of 3]
                                            └──► CreateStockSummary(List<MonthlyFinancialData>, List<MonthlyStockChange>) [used by 1 of 3]
```

to:

```
GetHybridWithCurrentMonthAsync ──┐
GetCachedFinancialOverview ──────┼──► BuildSummary(List<MonthlyFinancialDataDto>, bool) ──► CreateStockSummary(List<MonthlyFinancialDataDto>)  [sole overload]
GetFinancialOverviewRealTimeAsync┘
```

`GetFinancialOverviewRealTimeAsync` changes shape slightly: it must materialize its `Data` projection into a local `List<MonthlyFinancialDataDto>` *before* building the response, instead of building it inline inside the `Data = ...` property initializer, so the same list can be reused for both `Data` and `BuildSummary(...)`.

### Key Design Decisions

#### Decision 1: Where `BuildSummary` reads its stock-change source from

**Options considered:**
1. Keep two `CreateStockSummary` overloads, add `BuildSummary` as a thin wrapper that picks the overload based on which method calls it.
2. Collapse to a single `CreateStockSummary(List<MonthlyFinancialDataDto>)`, and make the real-time path materialize its DTO list earlier so it can feed the same overload as the other two paths.

**Chosen approach:** Option 2 — as specified in the spec's FR-2.

**Rationale:** Option 1 keeps duplication in spirit (two computation paths for the same output shape) even if it hides it behind one method name — it doesn't address the actual risk called out in the brief ("the two `CreateStockSummary` overloads already differ slightly"). Option 2 removes the second source of truth entirely. The spec's own analysis (see spec Background) demonstrates the two overloads are already numerically equivalent under the current 1:1 month-alignment invariant (`stockChangesLookup` construction already assumes uniqueness per `(Year, Month)`, or it would throw), so this is a safe consolidation, not a behavior change. Read the two current overloads yourself before implementing — they're at lines 504-518 (`List<MonthlyFinancialDataDto>` — keep this one) and 520-536 (`List<MonthlyFinancialData>, List<MonthlyStockChange>` — delete this one) of `FinancialAnalysisService.cs`.

#### Decision 2: `BuildSummary` parameter type — concrete DTO list vs. generic

**Options considered:**
1. `BuildSummary<T>(List<T> data, ...)` with an interface/generic constraint exposing `Income`, `Expenses`, `FinancialBalance`.
2. `BuildSummary(List<MonthlyFinancialDataDto> data, bool includeStockData)` — concrete type.

**Chosen approach:** Option 2, per the spec.

**Rationale:** All three call sites already have (or, for the real-time path, can cheaply obtain via Decision 3) a `List<MonthlyFinancialDataDto>` at the point the summary is built. A generic/interface abstraction would add a layer of indirection with zero current benefit — it's speculative generality for a case with exactly one concrete shape. This also sidesteps introducing a new interface into `Model/`, which would need its own justification under the "DTOs are never shared/global" guidance.

#### Decision 3: Materializing the real-time path's DTO list

**Options considered:**
1. Leave the `.Select(...)` projection inline inside `Data = ...` and call `MapToDto` a second time (or re-derive stock totals from the raw lists) just for the summary — i.e., compute the summary from a different representation than what's returned as `Data`.
2. Materialize the ordered `List<MonthlyFinancialDataDto>` into a local variable once, assign it to `Data`, and pass the same reference into `BuildSummary`.

**Chosen approach:** Option 2, per the spec.

**Rationale:** Computing `Data` and `Summary` from the same materialized list is the only way to guarantee `BuildSummary` is agnostic to which of the three call sites invoked it — it's what makes Decision 1 safe. It also removes a duplicate `MapToDto` invocation per month that today only exists because the projection was inline. This is a one-line structural change (introduce a local variable, no new loop, same O(n) cost — see spec NFR-1) with no behavior change.

## Implementation Guidance

### Directory / Module Structure

No new files. All changes are confined to the existing single file:
`backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs`

Do not create a new class/helper file for `BuildSummary` — it is a private implementation detail of `FinancialAnalysisService`, exactly like the existing `CalculatePeriodTotals` and `MapToDto` private statics in the same file. Splitting it out would be over-engineering for a single-consumer, single-file helper and would go against this file's established convention of keeping private computation helpers local.

### Interfaces and Contracts

No public interface changes. `IFinancialAnalysisService` (`backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/IFinancialAnalysisService.cs`) is untouched.

New private surface (exactly as specified in spec FR-1/FR-2 — implement verbatim, this is not open for reinterpretation):

```csharp
private static FinancialSummaryDto BuildSummary(List<MonthlyFinancialDataDto> data, bool includeStockData)
{
    return new FinancialSummaryDto
    {
        TotalIncome = data.Sum(d => d.Income),
        TotalExpenses = data.Sum(d => d.Expenses),
        TotalBalance = data.Sum(d => d.FinancialBalance),
        AverageMonthlyIncome = data.Any() ? data.Average(d => d.Income) : 0,
        AverageMonthlyExpenses = data.Any() ? data.Average(d => d.Expenses) : 0,
        AverageMonthlyBalance = data.Any() ? data.Average(d => d.FinancialBalance) : 0,
        StockSummary = includeStockData ? CreateStockSummary(data) : null
    };
}
```

Removed: `private static StockSummaryDto CreateStockSummary(List<MonthlyFinancialData> monthlyData, List<MonthlyStockChange> stockChanges)` (lines 520-536). The remaining `CreateStockSummary(List<MonthlyFinancialDataDto>)` (lines 504-518) is unchanged in body and becomes the sole overload.

All three call sites (`GetHybridWithCurrentMonthAsync` L317-330, `GetCachedFinancialOverview` L375-388, `GetFinancialOverviewRealTimeAsync` L477-497) replace their inline `new FinancialSummaryDto { ... }` block with `BuildSummary(<their list>, includeStockData)`.

### Data Flow

Two of the three call sites already have their `List<MonthlyFinancialDataDto>` in a local variable at the point of use (`allData` in the hybrid path, `orderedData` in the cached path) — those two just swap the inline block for `BuildSummary(allData, includeStockData)` / `BuildSummary(orderedData, includeStockData)`, no other change.

The real-time path is the only one requiring restructuring:

```csharp
var orderedData = monthlyData.OrderByDescending(d => d.Year).ThenByDescending(d => d.Month)
    .Select(d =>
    {
        var stockChangeData = stockChangesLookup.TryGetValue(new { d.Year, d.Month }, out var stockChange)
            ? stockChange
            : null;
        return MapToDto(d.Year, d.Month, d.Income, d.Expenses, stockChangeData, includeStockData);
    }).ToList();

var response = new GetFinancialOverviewResponse
{
    Data = orderedData,
    Summary = BuildSummary(orderedData, includeStockData)
};
```

This removes the now-unused `stockChangesList` parameter to the old two-arg `CreateStockSummary` — `stockChangesList` and `stockChangesLookup` are still needed to build `orderedData` via `MapToDto`, so they stay; only their use as a direct input to stock-summary computation goes away.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|------|------|
| Silent numeric drift in `StockSummary` for the real-time path once it's sourced from per-month DTO fields instead of the raw `stockChanges` list | Low | Spec Background already proves equivalence under the existing 1:1 month-uniqueness invariant (`stockChangesLookup` dictionary construction already assumes no duplicate `(Year, Month)`, or today's code throws). Add the FR-2 acceptance-criteria unit tests (real-time + cached path, with a seeded `MonthlyStockChange`) asserting the four `StockSummary` fields — these are new coverage, not modifications to existing tests, consistent with FR-3's "existing tests pass unmodified" constraint. |
| Reviewer/future-maintainer assumes `BuildSummary` needs to be `internal`/testable directly | Low | It doesn't — confirmed by reading `FinancialAnalysisServiceTests.cs`: all 8 existing tests exercise only the public `GetFinancialOverviewAsync`/`RefreshFinancialDataAsync`/`GetCacheStatus` methods via mocked `ILedgerService`/`IStockValueService`, none use reflection or `InternalsVisibleTo` to reach private members. Keep `BuildSummary` and the unified `CreateStockSummary` `private static`, exactly as the spec requires. |
| Scope creep — adding the hypothetical `ProfitMargin` field mentioned in the brief while already touching this code | Low | Explicitly out of scope per spec; do not add new `FinancialSummaryDto` fields in this change. |

## Specification Amendments

None. The spec (`artifacts/feat-3493/spec.r1.md`) is architecturally sound as written and matches what a direct read of `FinancialAnalysisService.cs` (571 lines) and `FinancialAnalysisServiceTests.cs` confirms:
- The three duplicated blocks and two `CreateStockSummary` overloads are exactly where and what the spec says (verified at lines 317-330, 375-388, 477-497, 504-518, 520-536).
- No existing test reaches private members or asserts on `StockSummary` fields today, so FR-3's "all existing tests pass unmodified" is achievable without any test changes, and the new tests proposed in the spec's Testing Approach are additive only.

## Prerequisites

None. No migrations, no config, no infrastructure changes — this is a same-file, same-PR refactor that can start immediately. Standard validation gate applies before completion: `dotnet build` and `dotnet format` (per repo-wide convention), plus the full existing + new `FinancialAnalysisServiceTests` suite green.
