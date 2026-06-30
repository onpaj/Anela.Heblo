# Design: Extract MapToDto Factory Method

## Component Design

### `FinancialAnalysisService` — private static `MapToDto`

A single private static method is added to consolidate `MonthlyFinancialDataDto` construction:

```
FinancialAnalysisService
  + MapToDto(year, month, income, expenses, stockChange?, includeStockData) : MonthlyFinancialDataDto
    - Computes financialBalance = income - expenses
    - Formats MonthYearDisplay = $"{month:D2}/{year}"
    - Conditionally populates StockChanges, TotalStockValueChange, TotalBalance
```

**Call sites after refactoring:**

| Method | Call |
|---|---|
| `GetHybridWithCurrentMonthAsync` | `MapToDto(now.Year, now.Month, income, expenses, stockChange, includeStockData)` |
| `GetCachedFinancialOverview` | `MapToDto(d.Year, d.Month, d.Income, d.Expenses, cachedStockData, includeStockData)` |
| `GetFinancialOverviewRealTimeAsync` | `MapToDto(d.Year, d.Month, d.Income, d.Expenses, stockChangeData, includeStockData)` |

**Placement:** immediately after the two existing `CreateStockSummary` overloads (currently lines 554–586), keeping all private static helpers together.

## Data Schemas

No schema changes. All existing types are used as-is:

| Type | Change |
|---|---|
| `MonthlyFinancialDataDto` | None — only its construction is consolidated |
| `StockChangeDto` | None |
| `MonthlyStockChange` | None — consumed as input to `MapToDto` |
| `IFinancialAnalysisService` | None — no public interface change |
