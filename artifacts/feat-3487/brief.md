## Module
Analytics

## Finding
`GetMarginReportRequest.IncludeDetailedBreakdown` (`backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetMarginReport/GetMarginReportRequest.cs:11`) is a public query parameter:

```csharp
public bool IncludeDetailedBreakdown { get; set; } = false;
```

`GetMarginReportHandler` never reads `request.IncludeDetailedBreakdown`. The handler always builds the full response — including both `ProductSummaries` and `CategorySummaries` — regardless of the flag value. No conditional branch on this property exists anywhere in the handler or its private helpers.

## Why it matters
Callers who pass `includeDetailedBreakdown=false` expecting a lighter response (e.g. for a summary widget that only needs totals) receive the full dataset. The public API implies an optimisation path that does not exist. It will mislead integrators reading the OpenAPI spec and adds noise to every request/response cycle.

## Suggested fix
Pick one option:
1. **Remove the parameter** from `GetMarginReportRequest` if no caller ever needs to suppress the breakdown (cleanest).
2. **Implement the flag**: when `IncludeDetailedBreakdown == false`, skip building `ProductSummaries` and `CategorySummaries` (set them to empty lists) and return only the aggregate totals.

Option 2 honours the documented intent and reduces payload size for summary consumers.

---
_Filed by daily arch-review routine on 2026-07-05._
