# Specification: Remove dead `getManufacturingSeverityColorClass` / `getManufacturingSeverityDisplayText` exports

## Summary
`frontend/src/api/hooks/useManufacturingStockAnalysis.ts` exports two helper functions, `getManufacturingSeverityColorClass` and `getManufacturingSeverityDisplayText`, that have zero consumers anywhere in the repository. `getManufacturingSeverityColorClass` also violates ADR-006 (every frontend color class must ship a `dark:` sibling) because it returns light-only Tailwind classes. This change removes both dead exports so the codebase carries no orphaned, dark-mode-unsafe helpers that a future developer could copy-paste into a new component.

## Background
The one component that would logically consume these helpers, `frontend/src/components/pages/ManufacturingStockAnalysis.tsx`, does not import them. It instead defines its own inline, dark-mode-aware severity-to-color mapping (e.g. `text-red-600 dark:text-red-400`, `text-orange-600 dark:text-orange-300` around lines 867/869/985/1002/1188/1207). This was verified by:
- Full-repo grep for `getManufacturingSeverityColorClass` and `getManufacturingSeverityDisplayText`: both symbols appear only at their own declaration sites (lines 126 and 146 of `useManufacturingStockAnalysis.ts`), with no import or call sites anywhere else, including the frontend unit test suite (`frontend/src/api/hooks/__tests__/useManufacturingStockAnalysis.test.tsx` has no reference to either function).
- Confirmation that `ManufacturingStockAnalysis.tsx` implements its own dark-mode-safe severity color logic independently, so removing the dead exports changes no runtime behavior.

This is a housekeeping / architecture-hygiene change filed by the daily arch-review routine, not a product feature request.

## Functional Requirements

### FR-1: Remove `getManufacturingSeverityColorClass`
Delete the exported function `getManufacturingSeverityColorClass` (lines 126–143 of `frontend/src/api/hooks/useManufacturingStockAnalysis.ts`), including its preceding `// Helper function to get severity color class` comment.

**Acceptance criteria:**
- The symbol `getManufacturingSeverityColorClass` no longer exists anywhere in the repository.
- No other file references it (already true today; verified by re-running the grep after the change to confirm zero remaining occurrences).

### FR-2: Remove `getManufacturingSeverityDisplayText` if confirmed unused
Delete the exported function `getManufacturingSeverityDisplayText` (lines 146–163 of the same file, including its preceding comment), since the analyst's grep confirmed it also has zero consumers.

**Acceptance criteria:**
- The symbol `getManufacturingSeverityDisplayText` no longer exists anywhere in the repository.
- No other file references it (already true today; verified by re-running the grep after the change).

### FR-3: No behavior change to `ManufacturingStockAnalysis.tsx` or any consumer
Because neither function has any consumer, no other file's code, imports, or behavior changes as a result of removing them.

**Acceptance criteria:**
- `frontend/src/components/pages/ManufacturingStockAnalysis.tsx` is unmodified.
- `frontend/src/api/hooks/useManufacturingStockAnalysis.ts` retains all other exports (`useManufacturingStockAnalysis` hook and any types/enums it re-exports) unchanged.
- Existing tests in `frontend/src/api/hooks/__tests__/useManufacturingStockAnalysis.test.tsx` and `frontend/src/components/pages/__tests__/ManufacturingStockAnalysis.test.tsx` continue to pass unmodified.

## Non-Functional Requirements

### NFR-1: Performance
Not applicable — this is a dead-code deletion with no runtime code path affected.

### NFR-2: Security
Not applicable — no security-sensitive code, data, or auth logic is touched.

### NFR-3: Standards compliance
The change eliminates the only ADR-006 violation identified in this finding (a Tailwind color-class helper lacking `dark:` variants). No new code is added that would need `dark:` variants, since the fix is deletion, not rewriting the helper to be dark-mode-safe.

## Data Model
Not applicable — no data model, DTO, or persisted entity is involved. `ManufacturingStockSeverity` (the enum consumed by the deleted functions) is untouched and continues to be used elsewhere (e.g. by `ManufacturingStockAnalysis.tsx` and the backend response models).

## API / Interface Design
Not applicable — no backend API, MediatR handler, or controller endpoint is touched. This is a frontend-only, non-exported-surface change confined to one TypeScript module's exports.

## Dependencies
None. The change has no dependency on other in-flight work and does not require backend changes, migrations, or API client regeneration (no OpenAPI-generated types are involved).

## Out of Scope
- Rewriting or reintroducing a dark-mode-safe version of a severity-to-color helper. Per the brief's suggested fix, if a future consumer needs severity-to-color mapping, it should be added fresh at that point with proper `dark:` variants — not resurrected from the deleted code.
- Any change to `ManufacturingStockAnalysis.tsx`'s existing inline color logic.
- Any change to the `ManufacturingStockSeverity` enum or backend severity computation logic.
- Broader dead-code audits of the `useManufacturingStockAnalysis.ts` file beyond the two named exports.

## Open Questions
None.

## Status: COMPLETE
