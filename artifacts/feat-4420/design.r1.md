# Design: Bank Statement Statistics DTO

## Component Design
- `DailyBankStatementStatisticsDto` (Application/Features/Analytics/Contracts): data-only class, no behavior.
- `GetBankStatementImportStatisticsResponse.Statistics`: `List<DailyBankStatementStatisticsDto>`.
- `GetBankStatementImportStatisticsHandler`: after the repository call, `dailyStatistics.Select(s => new DailyBankStatementStatisticsDto { Date = s.Date, ImportCount = s.ImportCount, TotalItemCount = s.TotalItemCount }).ToList()`.
- Frontend: re-export and use `DailyBankStatementStatisticsDto` in place of `DailyBankStatementStatistics`.

## Data Schemas
Response JSON (unchanged):
```json
{ "success": true, "statistics": [ { "date": "2025-10-01T00:00:00Z", "importCount": 2, "totalItemCount": 40 } ] }
```
OpenAPI schema `DailyBankStatementStatistics` is renamed `DailyBankStatementStatisticsDto`.
