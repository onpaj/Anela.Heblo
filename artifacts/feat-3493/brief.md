# [arch-review] FinancialOverview: FinancialSummaryDto construction duplicated 3× in FinancialAnalysisService

## Module
FinancialOverview

## Finding
The same `new FinancialSummaryDto { ... }` block is written three times inside `FinancialAnalysisService.cs` (`backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs`):

| Method | Lines |
|---|---|
| `GetHybridWithCurrentMonthAsync` | ~322–330 |
| `GetCachedFinancialOverview` | ~378–387 |
| `GetFinancialOverviewRealTimeAsync` | ~487–497 |

All three blocks compute the same six aggregates — `TotalIncome`, `TotalExpenses`, `TotalBalance`, `AverageMonthlyIncome`, `AverageMonthlyExpenses`, `AverageMonthlyBalance` — from a `List`, then conditionally appends a `StockSummary`. The only variation is which `CreateStockSummary` overload is called (DTO list vs domain list). Adding a new summary field (e.g. `ProfitMargin`) currently requires three edits in the same file.

## Why it matters
Real duplication (not just structural similarity) in a 570-line service file. Three touch-points for a single logical change increases the chance of an inconsistency being introduced — especially since the two `CreateStockSummary` overloads already differ slightly in how they compute `totalFinancialBalance`.

## Suggested fix
Extract a private method and unify the two `CreateStockSummary` overloads:

```csharp
private static FinancialSummaryDto BuildSummary(List data, bool includeStockData)
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

Replace all three `new FinancialSummaryDto { ... }` blocks with `BuildSummary(data, includeStockData)`. The overload `CreateStockSummary(List, List)` used by the real-time path should also be updated to map to DTOs first (or be removed) so a single `CreateStockSummary(List)` serves all three paths.

---
_Filed by daily arch-review routine on 2026-07-05._
