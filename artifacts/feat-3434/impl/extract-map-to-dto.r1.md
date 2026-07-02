# extract-map-to-dto.r1 — Implementation Summary

## Task
Extract a `MapToDto` private static factory method in `FinancialAnalysisService.cs` and replace all three inline `MonthlyFinancialDataDto` construction blocks.

## Changes made

**File:** `backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/FinancialAnalysisService.cs`

### Method added
`private static MonthlyFinancialDataDto MapToDto(int year, int month, decimal income, decimal expenses, MonthlyStockChange? stockChange, bool includeStockData)` — inserted at line 538, immediately after the second `CreateStockSummary` overload, before the class closing brace.

Key details:
- `MonthYearDisplay = $"{month:D2}/{year}"` (month-first format)
- `financialBalance` computed locally inside the method
- `TotalStockValueChange` uses `stockChange?.TotalStockValueChange` (no explicit cast — already `decimal`)
- `TotalBalance` and `TotalStockValueChange` are `null` when `includeStockData` is false

### Call sites replaced

| Site | Method | Before | After |
|------|--------|--------|-------|
| FR-2 | `GetHybridWithCurrentMonthAsync` | Local `financialBalance` + inline `new MonthlyFinancialDataDto { ... }` (21 lines) | `MapToDto(now.Year, now.Month, income, expenses, stockChange, includeStockData)` (1 line) |
| FR-3 | `GetCachedFinancialOverview` | `monthlyData.Add(new MonthlyFinancialDataDto { ... })` (19 lines) | `monthlyData.Add(MapToDto(...))` (6 lines) |
| FR-4 | `GetFinancialOverviewRealTimeAsync` | `.Select(d => { ... return new MonthlyFinancialDataDto { ... }; })` (18 lines) | `.Select(d => { var stockChangeData = ...; return MapToDto(...); })` (5 lines) |

The `var financialBalance = income - expenses;` local variable in `GetHybridWithCurrentMonthAsync` was removed (now computed inside `MapToDto`).

The `stockChangesLookup.TryGetValue(...)` lookup remains in the lambda body in FR-4 as required.

## Verification

- `dotnet build Anela.Heblo.sln` — 0 errors, 253 pre-existing warnings (none introduced)
- `dotnet format --verify-no-changes` on Application project — no formatting issues
- Net diff: 43 insertions, 59 deletions (net -16 lines)

## Commit

`8822651` — `refactor(FinancialOverview): extract MapToDto factory method (#3434)`  
Branch: `feature/3434-Arch-Review-Financialoverview-Monthlyfinancialdata`
