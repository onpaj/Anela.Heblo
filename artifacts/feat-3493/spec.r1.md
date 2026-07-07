# Specification: Deduplicate `FinancialSummaryDto` construction in `FinancialAnalysisService`

## Summary
`FinancialAnalysisService.cs` builds an identical six-field `FinancialSummaryDto` in three separate methods (`GetHybridWithCurrentMonthAsync`, `GetCachedFinancialOverview`, `GetFinancialOverviewRealTimeAsync`), and carries two `CreateStockSummary` overloads that differ in input shape and in how they source the stock-change total. This is a pure internal refactor: extract a single private `BuildSummary` helper and collapse the two `CreateStockSummary` overloads into one, with **zero change** to any value returned by the three public code paths. No public contracts, DTOs, or test-visible behavior change.

## Background
Issue #3493 (daily arch-review finding) flags real duplication across ~570 lines of `FinancialAnalysisService.cs`: three `new FinancialSummaryDto { ... }` blocks computing `TotalIncome`, `TotalExpenses`, `TotalBalance`, `AverageMonthlyIncome`, `AverageMonthlyExpenses`, `AverageMonthlyBalance`, plus a conditional `StockSummary`. Any future summary field (e.g. a hypothetical `ProfitMargin`) currently requires three synchronized edits, which is an unnecessary maintenance and consistency risk.

A closer read of the current source (as of this spec) shows the two `CreateStockSummary` overloads actually differ as follows:

- `CreateStockSummary(List<MonthlyFinancialDataDto> monthlyData)` — used by `GetHybridWithCurrentMonthAsync` and `GetCachedFinancialOverview`. Sums `d.TotalStockValueChange ?? 0` per DTO (i.e., the stock change already resolved and attached per month by `MapToDto`), and `d.FinancialBalance` (the DTO's precomputed `Income - Expenses`).
- `CreateStockSummary(List<MonthlyFinancialData> monthlyData, List<MonthlyStockChange> stockChanges)` — used only by `GetFinancialOverviewRealTimeAsync`. Sums `sc.TotalStockValueChange` directly over the raw `List<MonthlyStockChange>` fetched for the whole date range (not the per-month-matched DTO field), and `d.FinancialBalance` over the domain `MonthlyFinancialData` list (also `Income - Expenses`, same value as the DTO field).

So the `TotalFinancialBalance`/`AverageFinancialBalance` computation is already equivalent between the two overloads (same underlying `Income - Expenses` values, just read from different but value-equal fields). The only real discrepancy is the *source* of the stock-change aggregate: per-month DTO field (aligned 1:1 with the months actually returned) vs. a raw list sum. Under today's usage, both produce the same numeric result because `GetFinancialOverviewRealTimeAsync` fetches `stockChanges` over the exact same `[startDate, endDate]` range as `monthlyData`, with at most one `MonthlyStockChange` per distinct `(Year, Month)` (the existing `stockChangesLookup = stockChangesList.ToDictionary(sc => new { sc.Year, sc.Month }, ...)` call would itself throw on a duplicate month, so the code already assumes uniqueness). This refactor formalizes that equivalence by always routing through the DTO-based computation, matching the suggested fix in the issue ("map to DTOs first... so a single `CreateStockSummary(List<MonthlyFinancialDataDto>)` serves all three paths").

## Functional Requirements

### FR-1: Extract a single `BuildSummary` helper for `FinancialSummaryDto` construction
Add a private static method:

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

The parameter type is concretely `List<MonthlyFinancialDataDto>` (not a generic `List<T>`) — all three call sites already have, or can cheaply obtain, a `List<MonthlyFinancialDataDto>` at the point `FinancialSummaryDto` is built, so no generic constraint or interface is needed.

Replace all three existing `new FinancialSummaryDto { ... }` blocks (in `GetHybridWithCurrentMonthAsync`, `GetCachedFinancialOverview`, `GetFinancialOverviewRealTimeAsync`) with a call to `BuildSummary(data, includeStockData)`, passing the same `List<MonthlyFinancialDataDto>` each method already uses (or, for the real-time path, the materialized DTO list — see FR-2).

**Acceptance criteria:**
- Only one place in the file constructs `new FinancialSummaryDto { ... }`.
- `GetHybridWithCurrentMonthAsync`, `GetCachedFinancialOverview`, and `GetFinancialOverviewRealTimeAsync` each call `BuildSummary(...)` instead of inlining the object.
- For any given input data, the six numeric summary fields (`TotalIncome`, `TotalExpenses`, `TotalBalance`, `AverageMonthlyIncome`, `AverageMonthlyExpenses`, `AverageMonthlyBalance`) are byte-for-byte (value-for-value) identical to what the pre-refactor code produced for the same inputs.
- Public method signatures (`GetFinancialOverviewAsync`, `RefreshFinancialDataAsync`, `GetCacheStatus`) are unchanged.

### FR-2: Unify the two `CreateStockSummary` overloads into one
Remove `CreateStockSummary(List<MonthlyFinancialData> monthlyData, List<MonthlyStockChange> stockChanges)`. Keep only:

```csharp
private static StockSummaryDto CreateStockSummary(List<MonthlyFinancialDataDto> monthlyData)
{
    var totalStockChange = monthlyData.Sum(d => d.TotalStockValueChange ?? 0);
    var averageStockChange = monthlyData.Any() ? monthlyData.Average(d => d.TotalStockValueChange ?? 0) : 0;
    var totalFinancialBalance = monthlyData.Sum(d => d.FinancialBalance);
    var averageFinancialBalance = monthlyData.Any() ? monthlyData.Average(d => d.FinancialBalance) : 0;

    return new StockSummaryDto
    {
        TotalStockValueChange = totalStockChange,
        AverageMonthlyStockChange = averageStockChange,
        TotalBalanceWithStock = totalFinancialBalance + totalStockChange,
        AverageMonthlyTotalBalance = averageFinancialBalance + averageStockChange
    };
}
```

To make `GetFinancialOverviewRealTimeAsync` compatible with this single overload, materialize its ordered `List<MonthlyFinancialDataDto>` (the `.Select(d => MapToDto(...))` projection currently built inline inside the `Data = ...` initializer) into a local variable *before* constructing `GetFinancialOverviewResponse`, then:
- assign that local variable to `response.Data`, and
- pass the same local variable into `BuildSummary(...)` (which internally calls the unified `CreateStockSummary`).

This guarantees the stock-change aggregate is computed from the exact same per-month, already-resolved `TotalStockValueChange` values that are also returned to the caller in `Data` — eliminating the current dual source of truth (raw `stockChanges` list vs. per-month DTO field) while preserving the same numeric result under the existing 1:1 month-alignment assumption described in Background.

**Acceptance criteria:**
- Exactly one `CreateStockSummary` method remains in the file, taking `List<MonthlyFinancialDataDto>`.
- `GetFinancialOverviewRealTimeAsync` builds its DTO list once, reuses it for both `Data` and the summary/`StockSummary` computation — no duplicate `MapToDto` invocation for the same month.
- For every existing unit test in `FinancialAnalysisServiceTests.cs`, and for the manual scenarios below, `StockSummary` values (`TotalStockValueChange`, `AverageMonthlyStockChange`, `TotalBalanceWithStock`, `AverageMonthlyTotalBalance`) computed via the real-time path are numerically identical to what the pre-refactor two-overload code produced, for:
  - zero months of data,
  - months with no matching stock change (`TotalStockValueChange` treated as 0),
  - months with a matching stock change,
  - `includeStockData = false` (⇒ `StockSummary` stays `null`, unchanged).

### FR-3: No behavioral change to public API responses
This is a structural refactor only. `GetFinancialOverviewAsync` (the only public entry point that returns `GetFinancialOverviewResponse`) must return identical `Data` and `Summary` values before and after the change, for the same inputs and same underlying ledger/stock/cache state.

**Acceptance criteria:**
- All existing tests in `backend/test/Anela.Heblo.Tests/Application/FinancialOverview/FinancialAnalysisServiceTests.cs` pass unmodified.
- No new public members, no signature changes to `IFinancialAnalysisService`, `FinancialSummaryDto`, `StockSummaryDto`, or `MonthlyFinancialDataDto`.
- `dotnet build` and `dotnet format` succeed with no new warnings introduced by the refactor.

## Non-Functional Requirements

### NFR-1: Performance
No new I/O, no additional ledger/stock-value service calls, no additional cache lookups. The real-time path's DTO-list materialization replaces an inline LINQ projection with an equivalent materialized `.ToList()` assigned to a local variable — same O(n) cost, executed once instead of (implicitly) once already; no regression expected for the `months` sizes this service handles (≤ ~24).

### NFR-2: Security
Not applicable — no change to authentication, authorization, secrets, or data exposure. Purely internal, private-method refactor within an existing service.

## Data Model
No data model changes. Existing types used as-is:
- `Anela.Heblo.Application.Features.FinancialOverview.Model.FinancialSummaryDto`
- `Anela.Heblo.Application.Features.FinancialOverview.Model.StockSummaryDto`
- `Anela.Heblo.Application.Features.FinancialOverview.Model.MonthlyFinancialDataDto`
- `Anela.Heblo.Domain.Features.FinancialOverview.MonthlyFinancialData` (still used earlier in `GetFinancialOverviewRealTimeAsync` for per-month income/expense aggregation before mapping to DTOs; no longer passed into `CreateStockSummary`)
- `Anela.Heblo.Domain.Features.FinancialOverview.MonthlyStockChange` (still used to build `stockChangesLookup` for `MapToDto`; no longer passed into `CreateStockSummary`)

## API / Interface Design
No public interface changes. `IFinancialAnalysisService` and its single implementation `FinancialAnalysisService` keep the same public methods:
- `Task<GetFinancialOverviewResponse> GetFinancialOverviewAsync(...)`
- `Task RefreshFinancialDataAsync(...)`
- `FinancialAnalysisCacheStatus GetCacheStatus()`

Internal (private) surface changes only:
- New: `private static FinancialSummaryDto BuildSummary(List<MonthlyFinancialDataDto> data, bool includeStockData)`
- Changed: `private static StockSummaryDto CreateStockSummary(List<MonthlyFinancialDataDto> monthlyData)` (unchanged body, now the sole overload)
- Removed: `private static StockSummaryDto CreateStockSummary(List<MonthlyFinancialData> monthlyData, List<MonthlyStockChange> stockChanges)`
- Changed (internal restructuring only): `GetFinancialOverviewRealTimeAsync` now materializes its ordered `List<MonthlyFinancialDataDto>` into a local variable before building the response, instead of projecting inline inside the `Data = ...` property initializer.

## Dependencies
None beyond what the service already depends on (`ILedgerService`, `IStockValueService`, `IMemoryCache`, `IOptions<FinancialAnalysisOptions>`, `ILogger<FinancialAnalysisService>`). No new packages, no new project references.

## Testing Approach
- All existing tests in `FinancialAnalysisServiceTests.cs` must continue to pass without modification, since none of them assert against private members and none assert against `StockSummary` values today — they exercise routing/caching/date-range/income-expense behavior only.
- Add new unit tests (extension only, per task constraints — never weaken existing ones) covering the previously-untested `Summary`/`StockSummary` fields, e.g.:
  - A real-time-path test (empty cache, `includeStockData: true`) that stubs `IStockValueService.GetStockValueChangesAsync` to return a known `MonthlyStockChange` for one month and asserts `response.Summary.StockSummary.TotalStockValueChange`, `AverageMonthlyStockChange`, `TotalBalanceWithStock`, and `AverageMonthlyTotalBalance` match hand-computed expected values from `MonthlyStockChange.TotalStockValueChange` (i.e., the DTO-sourced computation, which by design now matches the previous raw-list computation under 1:1 month alignment).
  - A cached-path test (`GetCachedFinancialOverview`, via seeded `IMemoryCache` entries with a `MonthlyStockChange`) asserting the same four `StockSummary` fields for parity with the real-time-path test above, confirming the unified `CreateStockSummary` produces consistent results across both code paths.
  - An `includeStockData: false` test confirming `Summary.StockSummary` is `null` in all three paths (already implicitly covered by existing tests using `includeStockData: false`, but worth an explicit assertion once `BuildSummary` exists).

## Out of Scope
- Adding new summary fields (e.g., `ProfitMargin`) — the brief only asks to make future additions cheaper, not to add any now.
- Changing the caching strategy, cache keys, or `FinancialAnalysisOptions`.
- Changing `MapToDto`, `CalculatePeriodTotals`, `RefreshMonthlyDataAsync`, `GetCacheStatus`, or any date-range/routing logic in `GetFinancialOverviewAsync`.
- Changing `IStockValueService` or `ILedgerService` contracts.
- Any change to `FinancialSummaryDto`, `StockSummaryDto`, or `MonthlyFinancialDataDto` shape.
- Frontend/OpenAPI client regeneration — no public contract changes, so no client regeneration is triggered.

## Open Questions
None.

## Status: COMPLETE
