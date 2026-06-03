Write permission is needed for the artifacts directory. Let me output the implementation artifact content directly:

---

# Implementation: Remove Dead `StockSeverity.Severe` Member

## What was implemented
The task-plan called for removing the unreachable `Severe` member from the `StockSeverity` enum in the Purchase module and regenerating the TypeScript API client. Pre-implementation verification confirmed that commit `79178cac` (PR #1895 — "feat: Remove Unused `Severe` Member from `StockSeverity` Enum") already delivered the complete change before this branch was created. All acceptance criteria are satisfied by the existing commit; no new changes were required.

## Files created/modified
No files were modified in this session. The changes landed in PR #1895:

- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisResponse.cs` — `Severe,` line removed; remaining members: `Critical, Low, Optimal, Overstocked, NotConfigured`
- `frontend/src/api/generated/api-client.ts` — NSwag-regenerated; `Severe = "Severe",` absent from `StockSeverity`; `GiftPackageSeverity.Severe` unaffected

## Tests
No test changes needed. Purchase-module and full backend test suites verified green against current branch state.

## How to verify
```bash
grep -n "Severe" backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisResponse.cs
# Expected: no output

grep -A 8 "^export enum StockSeverity" frontend/src/api/generated/api-client.ts
# Expected: Critical, Low, Optimal, Overstocked, NotConfigured only

grep -nR "StockSeverity\.Severe" backend frontend/src --include="*.cs" --include="*.ts" --include="*.tsx"
# Expected: no output
```

## Notes
- **Spec/plan staleness:** All tasks in task-plan.r1.md were already completed by PR #1895 before this branch was created. The same applies to the Analytics spec — extraction done by PR #1847. The only remaining open item from the arch review (FR-3: SalesHistory date-filter consistency in `CatalogAnalyticsSourceAdapter.GetProductAnalysisDataAsync`) was not covered by the task-plan.
- **`GiftPackageSeverity.Severe` intentionally preserved** — distinct Logistics enum, not a target of this change.
- **Docs stale reference:** `docs/features/gift-package-manufacture.md:476` still reads `StockSeverity.Severe` where it should say `GiftPackageSeverity.Severe` — tracked for a separate docs PR.

## PR Summary
No PR needed — implementation pre-existed in commit `79178cac` (PR #1895), already merged to `main` and present on this branch.

### Changes
- *(no new changes — implementation pre-existed in commit `79178cac`)*

## Status
DONE