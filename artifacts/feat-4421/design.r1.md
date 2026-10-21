# Design: Correct misleading streaming comments in Analytics

## Component Design
No component changes. Only XML-doc / inline comments change in `MarginCalculator`, `GetProductMarginSummaryHandler`, `AnalyticsRepository`.

Target wording for `MarginCalculator.CalculateAsync` summary:
"Consumes products from an async stream and aggregates margin per group. Streaming avoids materialising a second full copy of the product list during fetch, but every accepted product (including its SalesHistory) is retained in `GroupProducts` because GetProductMarginSummaryHandler and MonthlyBreakdownGenerator need them. Peak memory is therefore O(products x sales history), not O(groups). Reducing it would require accumulating GroupMarginData inline instead."

Class summary: "Margin calculation logic extracted from the handler (single responsibility, testability)." (drop the memory claim). Remove the `🔒 PERFORMANCE FIX` memory claims at the other two sites similarly.

## Data Schemas
None; `MarginCalculationResult` unchanged.
