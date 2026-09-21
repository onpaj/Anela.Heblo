# Implementation: remove-dead-severity-helpers

## What was implemented
Deleted the two dead, unused exported helpers `getManufacturingSeverityColorClass` and `getManufacturingSeverityDisplayText` from `frontend/src/api/hooks/useManufacturingStockAnalysis.ts`, per the task context's step-by-step plan. Both had zero consumers anywhere in the repo and `getManufacturingSeverityColorClass` violated ADR-006 (no `dark:` Tailwind variants).

## Files created/modified
- `frontend/src/api/hooks/useManufacturingStockAnalysis.ts` — removed lines 125-164 (both helper functions, their preceding comments, and the blank line between them). No other line in the file was touched: the re-exported types/enums (`ManufacturingStockSeverity`, etc.), `getTimePeriodDisplayText`, and `useManufacturingStockAnalysisQuery` are unchanged.

## Tests
No new tests were added or modified, per spec FR-3 — the existing test suites for this module and its consumer do not reference the deleted symbols and needed no changes:
- `frontend/src/api/hooks/__tests__/useManufacturingStockAnalysis.test.tsx`
- `frontend/src/components/pages/__tests__/ManufacturingStockAnalysis.test.tsx`

## How to verify
1. `grep -rn "getManufacturingSeverityColorClass\|getManufacturingSeverityDisplayText" --include="*.ts" --include="*.tsx" .` — zero matches repo-wide.
2. `git diff -- frontend/src/api/hooks/useManufacturingStockAnalysis.ts` — single contiguous deletion hunk, nothing else changed.
3. `cd frontend && npm run build` — compiles successfully, no TypeScript errors.
4. `cd frontend && npm run lint` — no new lint findings on the touched file (pre-existing repo-wide `testing-library/*` lint findings in unrelated test files are untouched by this change).
5. `cd frontend && CI=true npx react-scripts test src/api/hooks/__tests__/useManufacturingStockAnalysis.test.tsx src/components/pages/__tests__/ManufacturingStockAnalysis.test.tsx --watchAll=false` — 2 suites / 37 tests pass.

## Notes
- The task context's Step 7 suggested `npx jest <files>` directly. That fails in this CRA (Create React App) project with a TypeScript parse error, because raw `npx jest` does not pick up react-scripts' Babel/TS transform config. The correct equivalent is `CI=true npx react-scripts test <files> --watchAll=false` (same command the `npm test` script wraps), which was used instead and passes cleanly. Documenting this as a deviation from the literal step text, not a change in what was verified.
- The worktree had no `node_modules` (not tracked by git). Since `package.json`/`package-lock.json` are byte-identical to the primary checkout's, a symlink to the primary checkout's `node_modules` was used to run build/lint/test without a lengthy reinstall, then removed before committing — it never touched tracked files.
- No code changes beyond the deletion; commit made directly per the developer agent's hard constraints (no worktree/branch creation, work committed on the current branch).

## PR Summary
Removed two dead, unused exported helper functions (`getManufacturingSeverityColorClass`, `getManufacturingSeverityDisplayText`) from `frontend/src/api/hooks/useManufacturingStockAnalysis.ts`. Neither had any consumers in the codebase, and the color-class helper returned hardcoded light-only Tailwind classes with no `dark:` variants, violating ADR-006 if it were ever reactivated. The component that would logically use severity-to-color mapping (`ManufacturingStockAnalysis.tsx`) already has its own inline, dark-mode-aware color logic and never imported either helper.

### Changes
- `frontend/src/api/hooks/useManufacturingStockAnalysis.ts` — deleted both dead helper functions and their comments (lines 125-164); no other line touched.

## Status
DONE
