# Specification: Deduplicate `FinancialSummaryDto` construction in `FinancialAnalysisService`

## Summary
`FinancialAnalysisService.cs` builds an identical `FinancialSummaryDto` (six aggregate fields plus a conditional `StockSummary`) in three separate methods, and maintains two near-duplicate `CreateStockSummary` overloads to support them. This refactor extracts a single private `BuildSummary` helper and collapses the two `CreateStockSummary` overloads into one, so a future change to the summary's shape or aggregation logic requires exactly one edit. This is a pure internal refactor: no public interface, DTO shape, or observable API/response behavior changes.

## Background
`IFinancialAnalysisService.GetFinancialOverviewAsync` routes to one of three internal calculation paths depending on cache state and request parameters:

- `GetHybridWithCurrentMonthAsync` (lines ~262–331) — current month computed in real time, prior months from cache.
- `GetCachedFinancialOverview` (lines ~333–389) — fully served from `IMemoryCache`.
- `GetFinancialOverviewRealTimeAsync` (lines ~391–502) — fully recomputed from `ILedgerService` / `IStockValueService`.

Each path ends by constructing a `GetFinancialOverviewResponse` whose `Summary` is a `new FinancialSummaryDto { ... }` block computing the same six aggregates (`TotalIncome`, `TotalExpenses`, `TotalBalance`, `AverageMonthlyIncome`, `AverageMonthlyExpenses`, `AverageMonthlyBalance`) from a list of monthly data, then conditionally attaching a `StockSummary` via one of two `CreateStockSummary` overloads:

- `CreateStockSummary(List<MonthlyFinancialDataDto> monthlyData)` (lines 504–518) — used by the hybrid and cached paths. Sums `TotalStockValueChange` and `FinancialBalance` directly off the already-built DTOs (each DTO's stock fields were populated per-month by `MapToDto`, matching stock data to its own month).
- `CreateStockSummary(List<MonthlyFinancialData> monthlyData, List<MonthlyStockChange> stockChanges)` (lines 520–536) — used only by the real-time path. Sums `FinancialBalance` off the domain `MonthlyFinancialData` list and `TotalStockValueChange` off the *raw* `stockChanges` list fetched for the whole date range, rather than off the per-month-matched DTOs.

Both overloads compute `FinancialBalance`/`totalFinancialBalance` with the same formula (`Income - Expenses`, summed), so in the normal case (one stock-change entry per month, matching the month range of the financial data) the two overloads produce numerically identical results. The duplication is therefore purely structural risk, not an existing behavioral divergence for normal inputs — but it does mean a future edge case or intentional change in one overload's aggregation could silently diverge from the other without any test catching it, since no test currently exercises `CreateStockSummary` in isolation or asserts `StockSummary` values against the real (non-mocked) `FinancialAnalysisService`.

Three call sites for the same logical block mean adding a new summary field (e.g. a hypothetical `ProfitMargin`) currently requires three synchronized edits.

## Functional Requirements

### FR-1: Extract a single `BuildSummary` helper
Add a private static method to `FinancialAnalysisService`:

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

**Acceptance criteria:**
- Method signature matches exactly (name, parameter types, return type, `private static`), placed in `FinancialAnalysisService` (e.g. adjacent to `CreateStockSummary`/`MapToDto`).
- All six aggregate computations are byte-for-byte identical (same LINQ expressions, same `.Any()` guard pattern) to what each of the three original inline blocks computed.

### FR-2: Unify the two `CreateStockSummary` overloads into one
Remove the `CreateStockSummary(List<MonthlyFinancialData>, List<MonthlyStockChange>)` overload (lines 520–536). Only `CreateStockSummary(List<MonthlyFinancialDataDto> monthlyData)` (lines 504–518) remains, unchanged in body.

**Acceptance criteria:**
- Exactly one `CreateStockSummary` method exists in the file after the change, with the DTO-list signature.
- Its implementation is unchanged from the current lines 504–518 (`TotalStockValueChange`/`FinancialBalance` summed off `List<MonthlyFinancialDataDto>`).
- No other file in the codebase references the removed overload (verify via a repo-wide search for `CreateStockSummary(` before deleting).

### FR-3: Restructure `GetFinancialOverviewRealTimeAsync` to produce the DTO list once and reuse it
Currently this method builds `response.Data` via a LINQ `.Select(...).ToList()` chain *and separately* builds `Summary.StockSummary` from the pre-DTO domain list (`monthlyData`) and raw `stockChangesList`. To let this path use the unified `BuildSummary`/`CreateStockSummary(List<MonthlyFinancialDataDto>)`, materialize the ordered DTO list into a local variable first, then use that same list for both `response.Data` and the call to `BuildSummary`:

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

**Acceptance criteria:**
- `response.Data` is assigned from the same materialized list instance passed to `BuildSummary`.
- The now-unused `stockChangesList` local variable (and its construction `stockChanges.ToList()` at line 449) is removed if no longer referenced after this change; `stockChangesLookup` (built from `stockChangesList`) is retained since it is still needed by the per-month DTO mapping. If `stockChangesList` is still needed only to build `stockChangesLookup`, keep the minimal code required for that and remove only the now-dead direct summation usage.
- `monthlyData.Sum(...)`/`monthlyData.Average(...)` calls that previously fed the inline `Summary` block are removed (superseded by `BuildSummary(orderedData, ...)`).

### FR-4: Replace all three inline `new FinancialSummaryDto { ... }` blocks with calls to `BuildSummary`
Replace:
- `GetHybridWithCurrentMonthAsync` (current lines 320–329): `Summary = BuildSummary(allData, includeStockData)`.
- `GetCachedFinancialOverview` (current lines 378–387): `Summary = BuildSummary(orderedData, includeStockData)` (using that method's existing `orderedData` local).
- `GetFinancialOverviewRealTimeAsync`: per FR-3, `Summary = BuildSummary(orderedData, includeStockData)` using the newly materialized local.

**Acceptance criteria:**
- No `new FinancialSummaryDto { ... }` object-initializer block remains anywhere in `FinancialAnalysisService.cs` except inside `BuildSummary` itself.
- Each of the three call sites passes the correct pre-existing `List<MonthlyFinancialDataDto>` local (`allData`, `orderedData` in `GetCachedFinancialOverview`, `orderedData` in `GetFinancialOverviewRealTimeAsync`) and the method's own `includeStockData` parameter.

### FR-5: Preserve behavior for existing callers and tests
No change to `IFinancialAnalysisService` (public interface), `GetFinancialOverviewResponse`, `FinancialSummaryDto`, `StockSummaryDto`, `MonthlyFinancialDataDto`, or any other public type. All three internal calculation paths must return numerically identical `Summary` values to before the refactor for any input that does not hit the pre-existing overload-divergence edge case described in Background (duplicate or out-of-range stock-change entries — see Open Questions/Assumptions).

**Acceptance criteria:**
- `backend/test/Anela.Heblo.Tests/Application/FinancialOverview/FinancialAnalysisServiceTests.cs` passes unchanged (no test modifications required).
- `backend/test/Anela.Heblo.Tests/Application/FinancialOverview/GetFinancialOverviewHandlerTests.cs` and `FinancialOverviewModuleTests.cs` pass unchanged (they exercise the handler/module against a mocked `IFinancialAnalysisService` and are unaffected by this internal refactor).
- `dotnet build` succeeds with no new warnings introduced in `FinancialAnalysisService.cs`.
- `dotnet format` produces no diff on the changed file.

## Non-Functional Requirements

### NFR-1: No behavior change (refactor-only)
This is a structural refactor. No new fields, endpoints, request parameters, or response shape changes. Output values for all three calculation paths must match pre-refactor output for all inputs currently covered by tests, and for all realistic production inputs (see Open Questions for the one identified theoretical edge case).

### NFR-2: Code style consistency
Match existing conventions in the file: private static helper methods placed near related helpers (`CreateStockSummary`, `MapToDto`), same XML/inline commenting density as surrounding code (minimal — the existing file has few comments on these blocks), same brace/indentation style as the rest of the file.

### NFR-3: Testability (optional, non-blocking)
Because `BuildSummary` and `CreateStockSummary` become the single source of truth for summary aggregation, they are natural candidates for direct unit testing in a follow-up. Not required for this task since they are `private static` and existing tests already exercise them indirectly through the three public code paths; adding direct tests is out of scope (see Out of Scope).

## Data Model
No data model changes. `FinancialSummaryDto`, `StockSummaryDto`, `MonthlyFinancialDataDto`, `MonthlyFinancialData`, and `MonthlyStockChange` are all unchanged (structure and semantics identical to current code, per `backend/src/Anela.Heblo.Application/Features/FinancialOverview/Model/*.cs` and `backend/src/Anela.Heblo.Domain/Features/FinancialOverview/*.cs`).

## API / Interface Design
N/A — this is an internal, private-method-only refactor within `FinancialAnalysisService`. `IFinancialAnalysisService` and all controller/MediatR-handler-facing contracts are unchanged. No new endpoints, no request/response DTO changes, no UI impact.

## Dependencies
- No new external dependencies.
- Depends on existing `ILedgerService`, `IStockValueService`, `IMemoryCache`, `FinancialAnalysisOptions` — all unchanged.
- File under change: `backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs`.
- Tests to run for verification: `backend/test/Anela.Heblo.Tests/Application/FinancialOverview/FinancialAnalysisServiceTests.cs`, `GetFinancialOverviewHandlerTests.cs`, `FinancialOverviewModuleTests.cs`.

## Out of Scope
- Adding new summary fields (e.g. `ProfitMargin`) — this refactor only makes such future additions cheaper, it does not add any.
- Adding new unit tests specifically targeting `BuildSummary`/`CreateStockSummary` in isolation (the private methods remain covered indirectly via existing public-path tests). May be proposed separately.
- Changing the caching strategy, cache key scheme, or the routing logic in `GetFinancialOverviewAsync` (hybrid/cached/real-time selection) — untouched by this refactor.
- Reconciling the theoretical edge-case divergence between the old two `CreateStockSummary` overloads for out-of-range or duplicate stock-change entries (see Open Questions) beyond what naturally falls out of unifying to the DTO-based overload.
- Any change to `MapToDto`, `CalculatePeriodTotals`, `RefreshMonthlyDataAsync`, `GetCacheStatus`, or `RefreshFinancialDataAsync` — none of these are touched by this refactor.

## Open Questions
None. (Resolved via assumption below.)

**Assumption (recorded, not requiring sign-off):** Unifying `CreateStockSummary` to the DTO-based overload changes how the real-time path (`GetFinancialOverviewRealTimeAsync`) sums `TotalStockValueChange` — from summing the raw `stockChanges` list fetched for the whole date range, to summing the per-month-matched value already attached to each `MonthlyFinancialDataDto` (via `stockChangesLookup`). Because `stockChangesLookup` is built with `ToDictionary(sc => new { sc.Year, sc.Month }, ...)`, which already throws on duplicate `(Year, Month)` keys, the existing code already assumes at most one stock-change entry per month within the fetched range. Under that existing assumption, the two computations are numerically equivalent for all realistic inputs (one stock-change entry per month, entirely within `[startDate, endDate]`). The only theoretical divergence is if `IStockValueService.GetStockValueChangesAsync` ever returned an entry for a year/month outside the `monthlyData` range it was queried for — an already-anomalous condition not exercised by any current test or expected from the service's contract. This refactor accepts that theoretical edge case as resolved in favor of the (now single) DTO-based aggregation, consistent with the brief's explicit instruction to make `CreateStockSummary(List<MonthlyFinancialDataDto>)` "serve all three paths."

## Status: COMPLETE
