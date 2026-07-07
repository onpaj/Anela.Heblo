# Design: Deduplicate `FinancialSummaryDto` construction in `FinancialAnalysisService`

## Component Design

No new components, services, or DI registrations. This design covers one class:
`backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs`.

### `BuildSummary` (new, private static)

```csharp
private static FinancialSummaryDto BuildSummary(List<MonthlyFinancialDataDto> data, bool includeStockData)
```

**Contract**
- Input: `data` — an already-materialized, per-month list of `MonthlyFinancialDataDto`. Order does not affect the result (all operations are `Sum`/`Average`/guarded-`Any`, not order-dependent). Caller owns the list's construction and ordering; `BuildSummary` does not mutate or re-sort it.
- Input: `includeStockData` — mirrors the same flag threaded through the calling path's `GetFinancialOverviewAsync` → `Get*` method → `MapToDto`. Must be the same value used to build `data` (i.e. don't pass `includeStockData: true` against a list built with `includeStockData: false`, since `TotalStockValueChange` would be `null` on every element and `StockSummary` would report all zeros instead of `null`).
- Output: a fully populated `FinancialSummaryDto`. Six aggregate fields (`TotalIncome`, `TotalExpenses`, `TotalBalance`, `AverageMonthlyIncome`, `AverageMonthlyExpenses`, `AverageMonthlyBalance`) are always computed from `data`. `StockSummary` is `CreateStockSummary(data)` when `includeStockData` is `true`, otherwise `null`.
- Side effects: none (pure function of its two arguments; no I/O, no cache access, no logging).
- Placement: declared adjacent to `CreateStockSummary`/`MapToDto`, after the three `Get*` orchestration methods, matching the file's existing public-API → private-orchestration → private-static-helper ordering.

### `CreateStockSummary` (unified, private static — body unchanged)

```csharp
private static StockSummaryDto CreateStockSummary(List<MonthlyFinancialDataDto> monthlyData)
```

**Contract**
- Input: `monthlyData` — the same per-month DTO list `BuildSummary` receives. Reads only `d.TotalStockValueChange` (nullable, coalesced to `0`) and `d.FinancialBalance` off each element; does not read `d.StockChanges` or any other field.
- Output: `StockSummaryDto` with `TotalStockValueChange`/`AverageMonthlyStockChange` summed/averaged from `TotalStockValueChange ?? 0`, and `TotalBalanceWithStock`/`AverageMonthlyTotalBalance` derived by adding those stock figures to the corresponding `FinancialBalance` sum/average.
- This is the sole remaining overload. The `(List<MonthlyFinancialData>, List<MonthlyStockChange>)` overload is deleted — it has no callers outside this file (verified by repo-wide grep in the architecture review) and is `private`, so removal cannot break any external caller.
- Behavioral note carried over from the spec/review: this overload sums the stock value already matched to each month's DTO (via `stockChangesLookup` inside `MapToDto`), rather than summing the raw stock-change list for the query range. The two were already proven numerically equivalent for all realistic inputs because `stockChangesLookup` is built with `ToDictionary`, which throws on a duplicate `(Year, Month)` key — so at most one stock-change entry exists per month in range.

### Call-site integration

All three existing orchestration methods keep their current responsibilities (fetch/compute monthly data, decide caching strategy, build `GetFinancialOverviewResponse.Data`); the only change is that each replaces its own inline `new FinancialSummaryDto { ... }` block with a call to `BuildSummary`, passing the `List<MonthlyFinancialDataDto>` local it already owns:

| Method | DTO list passed to `BuildSummary` | Notes |
|---|---|---|
| `GetHybridWithCurrentMonthAsync` | `allData` | Unchanged otherwise — current month computed live, prior months merged in from `GetCachedFinancialOverview`. |
| `GetCachedFinancialOverview` | `orderedData` | Unchanged otherwise — fully cache-sourced. |
| `GetFinancialOverviewRealTimeAsync` | `orderedData` (new local — see below) | Requires restructuring so the DTO list exists before `Summary` is built. |

**`GetFinancialOverviewRealTimeAsync` restructuring:** today this method computes `response.Data` via `monthlyData.OrderByDescending(...).Select(... MapToDto ...).ToList()` as part of the `GetFinancialOverviewResponse` initializer, while `Summary.StockSummary` is built separately from the pre-DTO `monthlyData` (domain type) and the raw `stockChangesList`. To integrate with the unified `BuildSummary`/`CreateStockSummary(List<MonthlyFinancialDataDto>)`, the `.Select(...).ToList()` chain is hoisted into a local variable (`orderedData`) *before* constructing `response`, and that same local is used for both `Data` and `Summary = BuildSummary(orderedData, includeStockData)`. This removes the method's second independent data path (domain-list + raw-stock-changes) in favor of a single materialized DTO list feeding both outputs. `stockChangesList`/`stockChangesLookup` are retained (still needed inside the `.Select` to attach per-month stock data via `MapToDto`); only their direct use as `CreateStockSummary(monthlyData, stockChangesList)` arguments is removed, along with the now-superseded `monthlyData.Sum(...)`/`monthlyData.Average(...)` calls that fed the old inline `Summary` block.

Boundary invariant: after the change, exactly one `new FinancialSummaryDto { ... }` object-initializer exists in the file — inside `BuildSummary` — and exactly one `CreateStockSummary` method exists, called only from `BuildSummary`.

## Data Schemas

No schema changes. The following shapes are unchanged by this refactor and are documented here only as the contract `BuildSummary`/`CreateStockSummary` operate against.

### `FinancialSummaryDto` (unchanged)
`backend/src/Anela.Heblo.Application/Features/FinancialOverview/Model/FinancialSummaryDto.cs`

```csharp
public class FinancialSummaryDto
{
    public decimal TotalIncome { get; set; }
    public decimal TotalExpenses { get; set; }
    public decimal TotalBalance { get; set; }
    public decimal AverageMonthlyIncome { get; set; }
    public decimal AverageMonthlyExpenses { get; set; }
    public decimal AverageMonthlyBalance { get; set; }
    public StockSummaryDto? StockSummary { get; set; }  // null unless includeStockData
}
```

### `StockSummaryDto` (unchanged)
`backend/src/Anela.Heblo.Application/Features/FinancialOverview/Model/StockSummaryDto.cs`

```csharp
public class StockSummaryDto
{
    public decimal TotalStockValueChange { get; set; }
    public decimal AverageMonthlyStockChange { get; set; }
    public decimal TotalBalanceWithStock { get; set; }      // TotalBalance + TotalStockValueChange
    public decimal AverageMonthlyTotalBalance { get; set; } // AverageMonthlyBalance + AverageMonthlyStockChange
}
```

### `MonthlyFinancialDataDto` (unchanged)
`backend/src/Anela.Heblo.Application/Features/FinancialOverview/Model/MonthlyFinancialDataDto.cs` — the element type of the lists `BuildSummary`/`CreateStockSummary` consume:

```csharp
public class MonthlyFinancialDataDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string MonthYearDisplay { get; set; }
    public decimal Income { get; set; }
    public decimal Expenses { get; set; }
    public decimal FinancialBalance { get; set; }
    public StockChangeDto? StockChanges { get; set; }
    public decimal? TotalStockValueChange { get; set; }  // null unless includeStockData; read by CreateStockSummary
    public decimal? TotalBalance { get; set; }
}
```

`IFinancialAnalysisService`, `GetFinancialOverviewResponse`, and all request/response contracts consumed by controllers or MediatR handlers are untouched — no new endpoints, fields, or response-shape changes result from this refactor.
