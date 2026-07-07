# Architecture Review: Deduplicate `FinancialSummaryDto` construction in `FinancialAnalysisService`

## Skip Design: true

This is a pure internal refactor of a single private-method-heavy backend service class. It touches no controller, no MediatR request/response contract, no DTO shape, no route, and no frontend code. Verified by inspecting the actual file: `IFinancialAnalysisService`, `GetFinancialOverviewResponse`, `FinancialSummaryDto`, `StockSummaryDto`, and `MonthlyFinancialDataDto` are all untouched — only two `private static` helpers inside `FinancialAnalysisService.cs` change. There is no UI surface to design.

## Architectural Fit Assessment

The change fits cleanly within existing conventions and requires no new architectural decisions:

- `FinancialAnalysisService` already lives at `backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs`, matching the documented layout rule in `docs/architecture/filesystem.md` (`Features/{Feature}/Services/` = "Domain services and business logic"). No file move is needed.
- `docs/architecture/development_guidelines.md` mandates that DTOs are classes, are owned by their module, and are never shared/global. `FinancialSummaryDto` and `StockSummaryDto` (in `Features/FinancialOverview/Model/`) are already classes and stay exactly as they are — the refactor only changes *how* an existing DTO instance is assembled, not its shape or ownership.
- The class already contains several `private static` helpers (`CalculatePeriodTotals`, `MapToDto`, the two `CreateStockSummary` overloads). Adding one more (`BuildSummary`) next to them is consistent with the file's existing internal style — no new layer, no new abstraction, no DI registration needed.
- I confirmed via a repo-wide grep that `CreateStockSummary(` is referenced nowhere outside `FinancialAnalysisService.cs` itself (one unrelated hit in an old planning doc under `docs/superpowers/plans/`). Removing the `(List<MonthlyFinancialData>, List<MonthlyStockChange>)` overload is safe — it has no external callers, and it is `private`, so it cannot be called from outside the class regardless.

**Behavioral-equivalence check (spec's core risk, verified against source):** I read all three call sites (lines 320–330, 378–387, 487–497) and both `CreateStockSummary` overloads (504–518, 520–536) directly.

- The DTO-based overload sums `d.TotalStockValueChange ?? 0` per `MonthlyFinancialDataDto`.
- The domain-list overload (only used by `GetFinancialOverviewRealTimeAsync`) sums `sc.TotalStockValueChange` directly off the *raw* `stockChangesList` fetched for the whole range, rather than off the per-month-matched value already stored on each DTO.
- Each DTO's `TotalStockValueChange` is set in `MapToDto` (line 563–565) from the same `stockChangesLookup` dictionary that the domain-list overload's caller builds at line 450 via `stockChangesList.ToDictionary(sc => new { sc.Year, sc.Month }, sc => sc)`. `Dictionary.ToDictionary` throws on a duplicate key, so the existing code *already* assumes at most one stock-change entry per `(Year, Month)` in the fetched range — the two summations are therefore mathematically the same sum over the same set of values, just reached via different intermediate collections (`sum over stockChanges` vs. `sum over DTOs' already-looked-up value`). The only case where they'd diverge is a stock-change entry for a year/month **outside** `[startDate, endDate]` that the lookup never attaches to any DTO — which is already an anomalous condition the current code doesn't guard against or test for. I agree with the spec's conclusion: this is a theoretical, pre-existing edge case, not a regression introduced by the refactor, and unifying on the DTO-based overload is the correct, lower-risk direction (it makes the "one entry per month, matched to its DTO" invariant the single source of truth instead of maintaining two).

No objection to the spec's FR-1 through FR-5. They are implementable as written.

## Proposed Architecture

### Component Overview

No new components. Internal-only restructuring of one existing class:

```
FinancialAnalysisService (unchanged public surface: IFinancialAnalysisService)
├── GetFinancialOverviewAsync (public, unchanged — routes to 3 private paths)
├── GetHybridWithCurrentMonthAsync (private)  ─┐
├── GetCachedFinancialOverview (private)       ├─→ Summary = BuildSummary(data, includeStockData)  [NEW shared call]
├── GetFinancialOverviewRealTimeAsync (private)─┘
├── BuildSummary(List<MonthlyFinancialDataDto>, bool) [NEW private static helper]
│     └── calls → CreateStockSummary(List<MonthlyFinancialDataDto>)  [the ONE remaining overload]
├── CreateStockSummary(List<MonthlyFinancialDataDto>) [KEPT, unchanged body]
├── CreateStockSummary(List<MonthlyFinancialData>, List<MonthlyStockChange>) [REMOVED]
└── MapToDto (private, unchanged)
```

### Key Design Decisions

#### Decision 1: Single `BuildSummary` helper operating on `List<MonthlyFinancialDataDto>`
**Options considered:**
1. Extract `BuildSummary` taking `List<MonthlyFinancialDataDto>` (as the spec proposes), requiring `GetFinancialOverviewRealTimeAsync` to materialize its DTO list before building the summary instead of after.
2. Extract `BuildSummary` as a generic/overloaded helper that accepts either domain or DTO lists, preserving today's two-overload split underneath.
3. Leave the three inline blocks as-is (no-op / reject the refactor).

**Chosen approach:** Option 1 — matches the spec (FR-1, FR-3) exactly.

**Rationale:** Option 2 preserves the exact duplication this task exists to remove (two summation code paths for stock data) and keeps the theoretical divergence risk alive indefinitely. Option 3 leaves three synchronized edit points for any future summary field. Option 1 is a strict simplification: `GetFinancialOverviewRealTimeAsync` already builds a DTO list for `response.Data` one statement later — hoisting that same `.Select(...).ToList()` above the `Summary` assignment and reusing the resulting local for both `Data` and `BuildSummary(...)` costs nothing extra (no additional allocation or iteration beyond what already happens) and removes the last reason for the second `CreateStockSummary` overload to exist.

#### Decision 2: Delete the `(List<MonthlyFinancialData>, List<MonthlyStockChange>)` overload rather than deprecate it
**Options considered:** Keep both overloads (mark one `[Obsolete]`) vs. delete the unused one outright.

**Chosen approach:** Delete outright.

**Rationale:** Both overloads are `private` — there is no external caller and no compatibility surface to preserve (confirmed by repo-wide grep: zero references outside this file). `[Obsolete]` markers exist to protect callers you don't control; that doesn't apply to a private method in a single-developer-maintained file. Deleting is the simplest correct action and directly satisfies FR-2.

## Implementation Guidance

### Directory / Module Structure
No new files, no new directories. All changes are confined to:
`backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs`

Place `BuildSummary` adjacent to `CreateStockSummary`/`MapToDto` (i.e., after the three `Get*` calculation methods, alongside the other `private static` helpers), matching NFR-2's placement guidance and the file's existing ordering (public API → private orchestration methods → private static computation helpers).

### Interfaces and Contracts
No public interface or contract changes. `IFinancialAnalysisService`, `GetFinancialOverviewResponse`, `FinancialSummaryDto`, `StockSummaryDto`, and `MonthlyFinancialDataDto` remain byte-for-byte unchanged. New private-only signature:

```csharp
private static FinancialSummaryDto BuildSummary(List<MonthlyFinancialDataDto> data, bool includeStockData)
```

Retained signature (unchanged body):
```csharp
private static StockSummaryDto CreateStockSummary(List<MonthlyFinancialDataDto> monthlyData)
```

Removed signature:
```csharp
private static StockSummaryDto CreateStockSummary(List<MonthlyFinancialData> monthlyData, List<MonthlyStockChange> stockChanges)
```

### Data Flow
Unchanged at the system level — same inputs (`ILedgerService`, `IStockValueService`, `IMemoryCache` data), same outputs (`GetFinancialOverviewResponse`). The only internal flow change is in `GetFinancialOverviewRealTimeAsync`: today it computes `Data` and `Summary.StockSummary` from two different intermediate representations (post-mapped DTOs for `Data`, pre-mapped domain list + raw stock-change list for `Summary`). After the refactor, `Data` and `Summary` are both derived from the same single materialized `List<MonthlyFinancialDataDto>` local, computed once and reused — one fewer independent data path through the method, which is a net reduction in this method's complexity, not just a line-count reduction.

Concretely (per spec FR-3), reorder so the DTO list exists before both usages:
```csharp
var orderedData = monthlyData.OrderByDescending(d => d.Year).ThenByDescending(d => d.Month)
    .Select(d => { ... return MapToDto(...); }).ToList();

var response = new GetFinancialOverviewResponse
{
    Data = orderedData,
    Summary = BuildSummary(orderedData, includeStockData)
};
```
`stockChangesList` (line 449) is still needed to build `stockChangesLookup` (line 450) for the per-month DTO mapping inside the `.Select`, so it is retained; only its direct use as a `CreateStockSummary` argument (old line 495) is removed.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Silent numeric divergence in `GetFinancialOverviewRealTimeAsync`'s `StockSummary` if a stock-change entry ever falls outside the queried date range (pre-existing theoretical edge case, not introduced by this refactor) | Low | Accept as-is per spec's Open Questions resolution — behavior converges to the DTO-based (per-month-matched) computation, which is the *more* correct one of the two, not the less correct one. No test currently exercises this edge case in either direction. |
| Regression in any of the three calculation paths' `Summary` values | Low | `FinancialAnalysisServiceTests.cs` already exercises all three paths and must pass unchanged (FR-5). Since `BuildSummary`'s body is a verbatim copy of the three existing inline blocks (verified line-by-line against source: same LINQ expressions, same `.Any()` guards), output is provably identical for any input where the sole variable — which `CreateStockSummary` overload runs — produces the same result (see Decision/Assessment above). |
| Reviewer/future-maintainer confusion about why the domain-typed `CreateStockSummary(List<MonthlyFinancialData>, List<MonthlyStockChange>)` overload disappeared | Low | A one-line comment is optional but not required; the removal is self-explanatory once `BuildSummary` and the single remaining `CreateStockSummary` are read together. Not worth NFR-2's "minimal comments" convention being broken for this. |

## Specification Amendments

None. The spec (`spec.r1.md`) is accurate against the real source file — I verified every cited line range (262–331, 333–389, 391–502, 504–518, 520–536, 538–570) and they match the actual file contents exactly, including the subtle `stockChangesLookup`/`ToDictionary` duplicate-key-throws reasoning used to justify behavioral equivalence. No corrections needed. Proceed with FR-1 through FR-5 as written.

One clarifying note for the implementer (not a spec change, just emphasis): when applying FR-3, do the reordering *before* deleting the old `Summary` block in `GetFinancialOverviewRealTimeAsync`, so at every intermediate step the file compiles — this avoids a window where `response.Data`'s `.Select(...)` and the old inline `Summary`'s domain-list computation both exist and could be edited inconsistently.

## Prerequisites

None. No migrations, no config, no infrastructure changes. This can be implemented directly against the current `main`/branch state. Standard verification applies: `dotnet build`, `dotnet format` (no diff expected on the changed file per NFR-2), and running the three test files listed in FR-5's acceptance criteria (`FinancialAnalysisServiceTests.cs`, `GetFinancialOverviewHandlerTests.cs`, `FinancialOverviewModuleTests.cs`) unchanged.
