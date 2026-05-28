## Module
Purchase

## Finding
The `StockSeverity` enum in the Purchase module declares a `Severe` member that is never used:

- File: `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisResponse.cs`
- Line 98: `Severe,`

Cross-checking all usage sites:
- `StockSeverityCalculator.DetermineStockSeverity` only returns `NotConfigured`, `Critical`, `Low`, `Overstocked`, or `Optimal` — never `Severe`.
- `ShouldIncludeItem` in `GetPurchaseStockAnalysisHandler` does not have a `StockStatusFilter` case for `Severe`.
- The frontend helpers `getSeverityColorClass` and `getSeverityDisplayText` in `usePurchaseStockAnalysis.ts` have no case for `Severe` (falls through to `default`).
- The generated TypeScript enum (`api-client.ts`) exposes `StockSeverity.Severe = "Severe"` to API consumers even though it can never be returned.

(Note: a separate `GiftPackageSeverity.Severe` in the Logistics module is unrelated.)

## Why it matters
A public enum value that can never be produced by the backend but is serialised into the generated API contract is misleading to frontend and API consumers. It wastes a switch branch in the frontend, bloats the TypeScript type, and adds cognitive overhead when reading the severity calculation logic.

## Suggested fix
Remove `Severe` from the `StockSeverity` enum. Then rebuild the generated TypeScript client (`npm run build` in frontend will regenerate `api-client.ts`). No logic changes are needed elsewhere because the value is never assigned.

---
_Filed by daily arch-review routine on 2026-05-27._