## Module
Analytics

## Finding

Three pairs of types are structurally identical — one lives in `Contracts/`, the other is a nested class inside the corresponding response type — and every handler copies from one to the other field-by-field with no transformation:

| Contracts/ type | Response nested class | Mapping location |
|---|---|---|
| `ProductMarginSummaryDto` (`Contracts/ProductMarginSummaryDto.cs`) | `GetMarginReportResponse.ProductMarginSummary` (`GetMarginReportResponse.cs:19–43`) | `GetMarginReportHandler.cs:161–179` |
| `CategoryMarginSummaryDto` (`Contracts/CategoryMarginSummaryDto.cs`) | `GetMarginReportResponse.CategoryMarginSummary` (`GetMarginReportResponse.cs:45–52`) | `GetMarginReportHandler.cs:181–191` |
| `MonthlyMarginBreakdownDto` (`Contracts/MonthlyMarginBreakdownDto.cs`) | `GetProductMarginAnalysisResponse.MonthlyMarginBreakdown` (`GetProductMarginAnalysisResponse.cs:20–25`) | `GetProductMarginAnalysisHandler.cs:93–102` |

Each mapping in the handlers is a verbatim property-copy: same field names, same types, no filtering or transformation.

## Why it matters

The parallel types serve no purpose. Each field added to the business logic (e.g. a new margin level) must be added in two places and the mapping updated. The intermediate DTO type and the nested response class add zero abstraction value when they are identical — this is redundant indirection that inflates maintenance cost and creates divergence risk, already observable: `MonthlyMarginBreakdownDto` has no `MarginPercentage` field while `ProductMarginSummaryDto` does — a natural divergence introduced during the duplication.

## Suggested fix

Drop the intermediate Contracts/ DTO and make the services that produce them return the response's nested type directly, or — if the Contracts/ DTO is the more stable shape — remove the nested class and have the response use the shared DTO directly. The handlers then eliminate the `.Select(dto => new ResponseNestedClass { ... })` mapping step entirely.

Concretely for `GetMarginReport`:

- Change `IReportBuilderService.BuildProductSummary` to return `GetMarginReportResponse.ProductMarginSummary` (or the other direction), removing the `ProductMarginSummaryDto` class.
- Same for `BuildCategorySummaries` / `CategoryMarginSummaryDto`.
- The handler's `ProductSummaries = reportData.ProductSummaries.Select(dto => new ... { ... }).ToList()` collapses to a direct assignment.

---
_Filed by daily arch-review routine on 2026-06-03._