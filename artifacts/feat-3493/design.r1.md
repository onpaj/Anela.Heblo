# Design: Deduplicate `FinancialSummaryDto` construction in `FinancialAnalysisService`

## Component Design

Single file affected: `backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs`. No new files, no public interface change to `IFinancialAnalysisService`.

### `BuildSummary` (new private static helper)

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

Responsibility: sole construction site for `FinancialSummaryDto`. Sits alongside the file's existing private static helpers (`CalculatePeriodTotals`, `MapToDto`) — same convention, no extraction to a separate class.

Called by all three existing methods, replacing their inline `new FinancialSummaryDto { ... }` blocks:
- `GetHybridWithCurrentMonthAsync` → `BuildSummary(allData, includeStockData)`
- `GetCachedFinancialOverview` → `BuildSummary(orderedData, includeStockData)`
- `GetFinancialOverviewRealTimeAsync` → `BuildSummary(orderedData, includeStockData)` (see restructuring below)

### `CreateStockSummary` (unified, single overload)

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

Body unchanged from the existing `List<MonthlyFinancialDataDto>` overload. The `List<MonthlyFinancialData>, List<MonthlyStockChange>` overload is deleted; there is no longer a code path that computes `StockSummaryDto` from raw domain lists.

### Call-site restructuring: `GetFinancialOverviewRealTimeAsync`

Only this method changes shape. Today it projects `Data` inline inside the response initializer and separately calls the two-arg `CreateStockSummary` using the raw `stockChangesList`. After the change, it materializes the DTO projection once and reuses it for both `Data` and `Summary`:

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

`stockChangesList` / `stockChangesLookup` remain — still required to resolve per-month `TotalStockValueChange` inside `MapToDto` — but are no longer passed directly into stock-summary computation. This removes the duplicate `MapToDto`-equivalent computation and the second source of truth for the stock-change aggregate.

### Call graph, before → after

```
Before:
GetHybridWithCurrentMonthAsync ──┐
GetCachedFinancialOverview ──────┼──► inline `new FinancialSummaryDto { ... }` (×3, duplicated)
GetFinancialOverviewRealTimeAsync┘         │
                                            ├──► CreateStockSummary(List<MonthlyFinancialDataDto>)   [2 of 3]
                                            └──► CreateStockSummary(List<MonthlyFinancialData>, List<MonthlyStockChange>) [1 of 3]

After:
GetHybridWithCurrentMonthAsync ──┐
GetCachedFinancialOverview ──────┼──► BuildSummary(List<MonthlyFinancialDataDto>, bool) ──► CreateStockSummary(List<MonthlyFinancialDataDto>)  [sole overload]
GetFinancialOverviewRealTimeAsync┘
```

## Data Schemas

No schema changes. Existing types are used as-is, with only the internal computation path consolidated:

- `FinancialSummaryDto`, `StockSummaryDto`, `MonthlyFinancialDataDto` (`Anela.Heblo.Application.Features.FinancialOverview.Model`) — shapes unchanged.
- `MonthlyFinancialData`, `MonthlyStockChange` (`Anela.Heblo.Domain.Features.FinancialOverview`) — still used earlier in `GetFinancialOverviewRealTimeAsync` to build `orderedData` via `MapToDto`; no longer passed into `CreateStockSummary`.
- `IFinancialAnalysisService` public methods (`GetFinancialOverviewAsync`, `RefreshFinancialDataAsync`, `GetCacheStatus`) — signatures and returned `GetFinancialOverviewResponse` shape unchanged; values must be identical before/after for the same inputs.

No new request/response shapes, no event payloads, no persistence or cache-key changes.
