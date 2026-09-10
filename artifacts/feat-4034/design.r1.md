# Design: Remove redundant combined `Task.WhenAll` in `CalculateMonthlyStockChangeAsync`

## Component Design
No new or modified components. This is a one-line dead-code deletion inside the existing private method `CalculateMonthlyStockChangeAsync` on `FinancialOverviewStockValueAdapter` (`backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/FinancialOverviewStockValueAdapter.cs`).

Delete the discarded-result await:
```
await Task.WhenAll(startStockTasks.Concat(endStockTasks));
```

The two result-capturing awaits immediately below it are kept unchanged and remain the sole means by which the method obtains its data:
```
var startValues = await Task.WhenAll(startStockTasks);
var endValues = await Task.WhenAll(endStockTasks);
```

`CalculateMonthlyStockChangeAsync` keeps its existing signature, visibility (private), return type, and its single caller (`GetStockValueChangesAsync`, once per month). Its sibling `GetStockValueChangeForPeriodAsync` already implements the same start/end concurrent-fetch shape without the redundant call and is unaffected. No public interface (`IStockValueService`), class relationship, or module boundary changes.

## Data Schemas
Not applicable. No database schema, API request/response shape, DTO (`MonthlyStockChange`, `StockChangeByType`), or event payload is created, modified, or removed by this change.
