# Architecture Review: Remove dead `getManufacturingSeverityColorClass` / `getManufacturingSeverityDisplayText` exports

## Skip Design: true

## Architectural Fit Assessment
This is a pure dead-code removal confined to one frontend module (`frontend/src/api/hooks/useManufacturingStockAnalysis.ts`). It introduces no new component, screen, or visual element, so it has no UI/UX surface and no design-phase work is required.

The module already re-exports several generated types (`GetManufacturingStockAnalysisResponse`, `ManufacturingStockItemDto`, `ManufacturingStockSummaryDto`, `ManufacturingStockSeverity`, `ManufacturingStockSortBy`) plus a `getTimePeriodDisplayText` helper imported from `../../utils/timePeriod`, alongside the `useManufacturingStockAnalysis` hook itself. `getManufacturingSeverityColorClass` and `getManufacturingSeverityDisplayText` sit at the bottom of the same file (lines 126–163) as standalone, hand-rolled helpers — not generated, not re-exported types. Removing them changes only this file's export surface; the module's role as a re-export/hook boundary for `ManufacturingStockAnalysis.tsx` and `ManufactureBatchPlanning.tsx` is unaffected because neither of those consumers imports the two dead helpers (confirmed by the analyst's grep in `spec.r1.md`).

This aligns with the project's existing convention of keeping color-class logic co-located with the component that renders it (`ManufacturingStockAnalysis.tsx` defines its own dark-mode-aware severity-color logic inline around lines 867/869), rather than in a shared hook module. The dead helpers were an inconsistent exception to that convention, not an established pattern — removing them brings the file back in line with how the rest of the module is used.

## Proposed Architecture

### Component Overview
```
frontend/src/api/hooks/useManufacturingStockAnalysis.ts
├── re-exported types/enums (unchanged)          ← keep
├── getTimePeriodDisplayText (unchanged)          ← keep
├── useManufacturingStockAnalysis() hook          ← keep
├── getManufacturingSeverityColorClass()          ← DELETE (dead, ADR-006 violation)
└── getManufacturingSeverityDisplayText()         ← DELETE (dead)

frontend/src/components/pages/ManufacturingStockAnalysis.tsx
└── inline severity→color mapping (dark-mode-aware, e.g. lines 867/869)  ← untouched, no import of deleted symbols
```
No new components, no changed data flow, no changed API contracts. This is a subtraction at the leaf of the dependency graph — nothing points into the two functions being removed.

### Key Design Decisions

#### Decision 1: Delete rather than fix
**Options considered:**
1. Delete both functions (per the brief's suggested fix).
2. Keep `getManufacturingSeverityColorClass` but add `dark:` variants to bring it into ADR-006 compliance, in case a future consumer needs it.
3. Keep both functions as-is and only document the risk.

**Chosen approach:** Delete both functions (Option 1).

**Rationale:** Both functions have zero consumers, verified by full-repo grep including the test suites. YAGNI applies directly — there is no current or planned use case driving the color-mapping shape (which color per severity, which shade for dark mode, whether it should return `bg-`/`text-` pairs or something else) so any `dark:` variants added now (Option 2) would be speculative and unverified against a real rendering context, and would still be dead code carrying a maintenance cost with zero test coverage forcing it to stay correct. Option 3 leaves the exact hazard the issue describes in place: a future developer greps for "severity color", finds this exported, untested, light-only helper, and copies it into a new component, silently breaking dark mode. Deleting is the only option that removes the hazard rather than deferring it.

## Implementation Guidance

### Directory / Module Structure
No new files or directories. Single-file edit:
- `frontend/src/api/hooks/useManufacturingStockAnalysis.ts` — remove lines 126–163 (both functions and their preceding `// Helper function to get severity ...` comments). Leave every other export (re-exported types/enums, `getTimePeriodDisplayText`, `useManufacturingStockAnalysis`) exactly as-is, including their surrounding comments and ordering.

### Interfaces and Contracts
No interface or contract changes. `ManufacturingStockSeverity` (the enum type consumed by the deleted functions) is a generated API-client type re-exported from this same file for other consumers — it is untouched and its re-export must remain, since `ManufacturingStockAnalysis.tsx` and `ManufactureBatchPlanning.tsx` depend on importing it from this module (see the existing in-file comment at lines 21–22).

Developers must NOT introduce a compensating re-export, shim, or "for future use" stub in place of the deleted functions — that would recreate the exact dead-code smell this change removes.

### Data Flow
Unaffected. No runtime code path currently calls either deleted function, so no data flow through the application changes. `ManufacturingStockAnalysis.tsx` continues to compute its own severity-to-color mapping inline exactly as it does today.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| A consumer imports these functions dynamically (e.g. via string-based lookup) that a static grep would miss | Low | The analyst's grep matched the bare identifiers repo-wide (not just static `import` statements), which would also catch dynamic `require`/computed-property usage referencing the same string. No such usage was found. Re-run the grep after deletion as a final check (FR-1/FR-2 acceptance criteria) before merging. |
| TypeScript build or lint fails to catch an unnoticed external consumer (e.g. in a file excluded from the TS project) | Low | `npm run build` and `npm run lint` (already required by project validation gates) will fail on any remaining unresolved import if a consumer was missed, since these are named exports, not `export *`. |
| Deleting the display-text helper turns out to be premature if a hidden non-code reference exists (e.g. Storybook, docs snippet) | Low | Grep was full-repo, not scoped to `frontend/src`, so it would also catch references in docs, tests, or config. None found. |

## Specification Amendments
None. `spec.r1.md` FR-1/FR-2/FR-3 already correctly scope this as a two-function deletion with no consumer impact; no architectural finding here changes that scope.

## Prerequisites
None. No migration, config, or infrastructure change is needed before implementation can start — this can be implemented directly as a single-file edit.
