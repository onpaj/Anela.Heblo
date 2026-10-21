# Design: Named account-class constants

## Component Design
`FinancialAnalysisService` gains two private consts next to the existing cache-key consts:

```csharp
// Czech Chart of Accounts: class 5 = expenses (náklady), class 6 = revenues (výnosy)
private const string ExpenseAccountClassPrefix = "5";
private const string IncomeAccountClassPrefix = "6";
```
`CalculatePeriodTotals` uses them in its four `StartsWith(...)` calls.

## Data Schemas
None.
