# Architecture Review: Remove dead severity-formatting exports from `usePurchaseStockAnalysis.ts`

## Skip Design: true

## Architectural Fit Assessment
This is dead-code removal, not feature work. There is no architectural surface to fit: no new module, no new contract, no cross-boundary dependency. The two functions (`getSeverityColorClass`, `getSeverityDisplayText`) are pure, side-effect-free string formatters that happen to be co-located in a React Query hook file but have no relationship to the query logic itself. Verified directly against the working tree:

- `frontend/src/api/hooks/usePurchaseStockAnalysis.ts` exports `usePurchaseStockAnalysisQuery`, the two dead helpers (lines 72–109), then `formatNumber`/`formatCurrency` (lines 112–130), which are genuinely consumed elsewhere.
- Project-wide grep (`grep -rn "getSeverityColorClass\|getSeverityDisplayText" frontend/src`) returns only the two `export const` definitions at lines 72 and 92 — no import, no call site, no test reference, anywhere in `frontend/src`.
- `frontend/src/components/pages/PurchaseStockAnalysis.tsx`, the natural consumer, imports `StockSeverity` directly from the generated API client and implements its own inline `getRowColorClass` (line 251) and `getSeverityStripColor` (line 292) — it never delegates to the hook's exports. Removal changes nothing for this page.
- `getSeverityColorClass` returns light-mode-only classes (e.g. `text-red-600 bg-red-50`, no `dark:` variant), which is a latent ADR-006 violation. Since it's dead code, deleting it is strictly better than fixing it — no reason to invest in dark-mode variants for code nobody calls.

Aligns with YAGNI and the repo's "surgical changes" norm: delete only what's unused, touch nothing else in the file.

## Proposed Architecture

### Component Overview
No components change. This is a subtraction from one existing file:

```
frontend/src/api/hooks/usePurchaseStockAnalysis.ts
├── usePurchaseStockAnalysisQuery   (kept, untouched)
├── getSeverityColorClass           (DELETE, lines 72-89 + comment)
├── getSeverityDisplayText          (DELETE, lines 92-109 + comment)
├── formatNumber                    (kept, untouched)
└── formatCurrency                  (kept, untouched)
```

No other file changes. No new files.

### Key Design Decisions

#### Decision 1: Delete vs. fix-and-keep
**Options considered:**
1. Delete both functions (spec's proposal).
2. Keep them but add `dark:` variants to make them ADR-006-compliant "for future use."

**Chosen approach:** Delete.

**Rationale:** Option 2 speculatively maintains code with zero consumers — the definition of YAGNI violation the brief calls out. If a future screen needs severity-to-class/text mapping, it should be written fresh, co-located with its consumer, and reviewed for ADR-006 compliance at that time (as `PurchaseStockAnalysis.tsx` already does independently with its own `getRowColorClass`/`getSeverityStripColor`). Keeping unused code "just in case" is exactly the pattern this codebase's arch-review process exists to catch.

#### Decision 2: Scope of the diff
**Options considered:**
1. Remove only the two functions and their preceding comments.
2. Also touch `formatNumber`/`formatCurrency` or reorganize the file while in there.

**Chosen approach:** Option 1 — strictly the two functions and their comments.

**Rationale:** Matches CLAUDE.md's "surgical changes" rule and FR-3's acceptance criterion that `git diff` show only the two removals. No adjacent cleanup, no reformatting.

## Implementation Guidance

### Directory / Module Structure
No structural change. Edit in place: `frontend/src/api/hooks/usePurchaseStockAnalysis.ts`.

### Interfaces and Contracts
None affected. `GetPurchaseStockAnalysisRequest`, the re-exported generated types (`StockStatusFilter`, `StockAnalysisSortBy`, `StockSeverity`, `StockAnalysisItemDto`, `LastPurchaseInfoDto`, `StockAnalysisSummaryDto`, `GetPurchaseStockAnalysisResponse`), and `stockAnalysisKeys` are untouched. `usePurchaseStockAnalysisQuery`'s signature and behavior are unchanged.

### Data Flow
Unaffected — no runtime code path currently calls either function, so no data flow changes.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Hidden consumer missed by grep (e.g. dynamic import, string-based reflection) | Very Low | Grep covers all of `frontend/src` for both exact identifiers; TypeScript's build step (`npm run build`) will fail loudly on any missed static import, and `tsc`/ESLint would flag an unused-import if one existed. |
| Test file references the removed exports | Very Low | `PurchaseStockAnalysis.test.tsx` was checked in the spec's verification pass; it does not import either function. Run `npm run build` and the existing test suite as a final check per FR-3. |

## Specification Amendments
None. The spec is complete, correctly scoped, and its acceptance criteria are directly verifiable via `grep` and `git diff`. No architectural gaps were found during review.

## Prerequisites
None. No migrations, config, or infrastructure changes are needed — this is a self-contained frontend edit ready for immediate implementation.
