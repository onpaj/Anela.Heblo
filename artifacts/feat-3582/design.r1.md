# Design: Remove dead severity-formatting exports from `usePurchaseStockAnalysis.ts`

No UI or data schema changes are involved. This is a pure dead-code deletion in a single frontend file, with no user-facing surface and no request/response, event, or database shape affected.

## Component Design
The only change is a subtraction from `frontend/src/api/hooks/usePurchaseStockAnalysis.ts`: delete the two dead exports `getSeverityColorClass` (lines 72–89, plus its preceding comment) and `getSeverityDisplayText` (lines 92–109, plus its preceding comment). Both are pure, side-effect-free string formatters with zero consumers anywhere in `frontend/src` — confirmed by project-wide grep in both the spec and the architecture review.

`usePurchaseStockAnalysisQuery`, `formatNumber`, `formatCurrency`, the re-exported generated types (`StockStatusFilter`, `StockAnalysisSortBy`, `StockSeverity`, `StockAnalysisItemDto`, `LastPurchaseInfoDto`, `StockAnalysisSummaryDto`, `GetPurchaseStockAnalysisResponse`), the `GetPurchaseStockAnalysisRequest` interface, and the `stockAnalysisKeys` query-key factory all remain in the file, untouched.

There is no other consumer to coordinate with: `PurchaseStockAnalysis.tsx`, the natural candidate, never imports either function — it implements its own inline `getRowColorClass` and `getSeverityStripColor` for severity-based styling. The deletion is therefore self-contained to one file, with no interface, contract, or call-site changes elsewhere.

## Data Schemas
Not applicable — no database, API, or event payload changes.
