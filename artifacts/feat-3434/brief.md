## Module
FinancialOverview

## Finding
`backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs` constructs `MonthlyFinancialDataDto` with the same mapping logic (income, expenses, financialBalance, conditionally-populated stock fields) in **three separate methods**:

| Method | Approximate lines |
|---|---|
| `GetHybridWithCurrentMonthAsync` | 303–321 |
| `GetCachedFinancialOverview` | 381–398 |
| `GetFinancialOverviewRealTimeAsync` | 514–535 |

All three build the same object shape:
```csharp
new MonthlyFinancialDataDto
{
    Year = ..., Month = ..., MonthYearDisplay = ...,
    Income = ..., Expenses = ..., FinancialBalance = ...,
    StockChanges = includeStockData && stockChange != null ? new StockChangeDto { ... } : null,
    TotalStockValueChange = includeStockData ? (stockChange?.TotalStockValueChange ?? 0) : null,
    TotalBalance = includeStockData ? ... + ... : null
}
```

The logic is non-trivial (conditional stock field population, null-coalescing) and is materially identical across all three call-sites.

## Why it matters
Any change to `MonthlyFinancialDataDto` shape or to the stock-inclusion logic (e.g., a new field, a change to what `null` means for `TotalStockValueChange`) requires three synchronized edits. Missing one creates a data inconsistency between the cached, hybrid, and real-time paths — which the caller has no way to detect. The 10-line block appearing three times exceeds the "three similar lines" DRY threshold in the project guidelines.

## Suggested fix
Extract a private factory method alongside the existing two `CreateStockSummary` overloads:

```csharp
private static MonthlyFinancialDataDto MapToDto(
    int year, int month,
    decimal income, decimal expenses,
    MonthlyStockChange? stockChange,
    bool includeStockData)
{
    var financialBalance = income - expenses;
    return new MonthlyFinancialDataDto
    {
        Year = year, Month = month,
        MonthYearDisplay = $"{month:D2}/{year}",
        Income = income, Expenses = expenses,
        FinancialBalance = financialBalance,
        StockChanges = includeStockData && stockChange != null
            ? new StockChangeDto { Materials = stockChange.StockChanges.Materials, ... }
            : null,
        TotalStockValueChange = includeStockData ? (stockChange?.TotalStockValueChange ?? 0) : null,
        TotalBalance = includeStockData
            ? financialBalance + (stockChange?.TotalStockValueChange ?? 0)
            : null
    };
}
```

Replace all three construction sites with a call to `MapToDto(...)`. No behavioral change; the three paths already produce identical shapes.

---
_Filed by daily arch-review routine on 2026-06-30._
