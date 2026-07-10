# Specification: Remove dead severity-formatting exports from `usePurchaseStockAnalysis.ts`

## Summary
`frontend/src/api/hooks/usePurchaseStockAnalysis.ts` exports two helper functions, `getSeverityColorClass` and `getSeverityDisplayText`, that have zero consumers anywhere in the frontend codebase. This spec covers removing both dead exports to reduce the hook's public surface and eliminate a latent dark-mode violation that would trigger if either function were ever wired up.

## Background
The Purchase Stock Analysis feature exposes a React Query hook (`usePurchaseStockAnalysisQuery`) in `frontend/src/api/hooks/usePurchaseStockAnalysis.ts`, alongside several standalone helper functions re-exported from the same module: `getSeverityColorClass` (line 72), `getSeverityDisplayText` (line 92), `formatNumber` (line 112), and `formatCurrency` (line 124).

Verification performed against the working tree at `/home/user/worktrees/feature-3582-Arch-Review-Purchase-Getseveritycolorclass-And-Get`:

- Read the full contents of `usePurchaseStockAnalysis.ts`. `getSeverityColorClass` (lines 72–89) and `getSeverityDisplayText` (lines 92–109) are confirmed to exist as described, immediately followed by `formatNumber` and `formatCurrency`.
- Ran `grep -rn "getSeverityColorClass\|getSeverityDisplayText" frontend/src` — the only two matches in the entire `frontend/src` tree are the two `export const` definitions themselves at lines 72 and 92. No file imports or calls either function. This confirms the brief's claim: both are dead exports with zero consumers.
- Read `frontend/src/components/pages/PurchaseStockAnalysis.tsx`, the natural consumer page. It imports `StockSeverity` directly from `../../api/generated/api-client` (not the two helpers) and implements its own inline severity-to-styling logic via two locally defined functions: `getRowColorClass` (line 251, row background tinting) and `getSeverityStripColor` (line 292, color strip). Neither delegates to the hook's exported helpers.
- Confirmed the dark-mode concern: `getSeverityColorClass` returns hardcoded classes such as `"text-red-600 bg-red-50"` with no `dark:` variant. Per `memory/decisions/light-dark-mode-required.md` (which implements ADR-006, defined in `docs/architecture/development_guidelines.md`), every frontend component/function that renders color must have both light and dark styling before it can be considered usable. `getSeverityColorClass` does not meet this bar. Because it is unused, it is not currently a live violation, but it would become one immediately if any future code imported it as-is.
- `formatNumber` and `formatCurrency`, defined in the same file directly below the two dead exports, are excluded from this change — they are actively imported and used elsewhere (e.g., by `PurchaseStockAnalysis.tsx`) and are out of scope.

This is a pure dead-code removal (YAGNI cleanup) identified during an architecture review pass over the Purchase module. No behavior change is intended for any active code path.

## Functional Requirements

### FR-1: Remove `getSeverityColorClass` export
Delete the `getSeverityColorClass` function (currently lines 72–89 of `frontend/src/api/hooks/usePurchaseStockAnalysis.ts`), including its preceding comment (`// Helper function to get severity color class`).

**Acceptance criteria:**
- `getSeverityColorClass` no longer appears anywhere in `frontend/src/api/hooks/usePurchaseStockAnalysis.ts`.
- A project-wide search (`grep -rn "getSeverityColorClass" frontend/src`) returns zero matches.

### FR-2: Remove `getSeverityDisplayText` export
Delete the `getSeverityDisplayText` function (currently lines 92–109 of `frontend/src/api/hooks/usePurchaseStockAnalysis.ts`), including its preceding comment (`// Helper function to get severity display text`).

**Acceptance criteria:**
- `getSeverityDisplayText` no longer appears anywhere in `frontend/src/api/hooks/usePurchaseStockAnalysis.ts`.
- A project-wide search (`grep -rn "getSeverityDisplayText" frontend/src`) returns zero matches.

### FR-3: Preserve all remaining exports and behavior unchanged
`usePurchaseStockAnalysisQuery`, `formatNumber`, `formatCurrency`, the re-exported generated types (`StockStatusFilter`, `StockAnalysisSortBy`, `StockSeverity`, `StockAnalysisItemDto`, `LastPurchaseInfoDto`, `StockAnalysisSummaryDto`, `GetPurchaseStockAnalysisResponse`), the `GetPurchaseStockAnalysisRequest` interface, and the `stockAnalysisKeys` query-key factory must remain exactly as they are today — no signature, behavior, or formatting changes beyond removing the two dead functions.

**Acceptance criteria:**
- `git diff` on `usePurchaseStockAnalysis.ts` shows only the removal of the two functions (and their comments); no other lines are touched.
- `PurchaseStockAnalysis.tsx` and its test file `frontend/src/components/pages/__tests__/PurchaseStockAnalysis.test.tsx` require no changes and continue to pass, since neither imports the removed functions.

## Non-Functional Requirements

### NFR-1: Performance
Not applicable — this is a dead-code removal with no runtime code paths affected. No measurable performance impact (a negligible reduction in bundle size from removing ~18 lines of dead code).

### NFR-2: Security
Not applicable — no security-sensitive code, data, or auth logic is involved.

## Data Model
Not applicable — no data model changes. `StockSeverity` enum (from generated API client) is unaffected; it continues to be used by `PurchaseStockAnalysis.tsx`'s own inline severity-styling functions.

## API / Interface Design
Not applicable — no backend API changes. This is a frontend-only removal of two unused TypeScript function exports; no public component props, hooks signatures, or HTTP endpoints change.

## Dependencies
- None. This change is self-contained within `frontend/src/api/hooks/usePurchaseStockAnalysis.ts` and has no upstream or downstream dependencies, since the removed functions have no consumers.

## Out of Scope
- Any change to `formatNumber` or `formatCurrency` in the same file — both are actively used and unaffected.
- Any change to `PurchaseStockAnalysis.tsx`'s own inline `getRowColorClass` / `getSeverityStripColor` functions, which already implement severity-based coloring independently (including dark-mode-aware classes where applicable) and are not part of this cleanup.
- Adding dark-mode variants to the removed functions — since they are being deleted rather than activated, ADR-006 compliance work is unnecessary.
- Broader dead-code audits of other hooks or modules — this spec is scoped to the two named exports in the Purchase module only.

## Open Questions
None.

## Status: COMPLETE
