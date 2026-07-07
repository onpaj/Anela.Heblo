# Implementation: wire-consumers

## What was implemented
Updated the two grid-consumer pages so the app compiles again against the new `useGridLayout` / `GridHeader` contract introduced by the prior two tasks. Both pages now destructure `setColumnWidthLive` and `commitColumnWidth` from `useGridLayout` (instead of the removed `setColumnWidth`) and wire them to `GridHeader`'s new `onResizeChange` (continuous, no save) and `onResizeEnd` (once, debounced save) props respectively. Also updated the two page-level test mocks of `useGridLayout` that still stubbed the removed `setColumnWidth`, so the grep-based "no dangling references" check passes cleanly.

## Files created/modified
- `frontend/src/components/pages/ManufacturingStockAnalysis.tsx` — destructure updated to `setColumnWidthLive`/`commitColumnWidth` (was `setColumnWidth`); `GridHeader` JSX now passes `onResizeChange={setColumnWidthLive}` and `onResizeEnd={commitColumnWidth}` (was single `onResizeEnd={setColumnWidth}`).
- `frontend/src/components/pages/PurchaseStockAnalysis.tsx` — same change as above.
- `frontend/src/components/pages/__tests__/ManufacturingStockAnalysis.test.tsx` — `useGridLayout` mock updated to stub `setColumnWidthLive` and `commitColumnWidth` instead of `setColumnWidth` (not explicitly listed in the task file, but required to satisfy the grep verification step and keep the mock shape consistent with the real hook's new return type).
- `frontend/src/components/pages/__tests__/PurchaseStockAnalysis.test.tsx` — same change as above.

## Tests
- `grep -rn "setColumnWidth\b" frontend/src` — no matches (confirmed no dangling references to the removed function; only `setColumnWidthLive` / `commitColumnWidth` remain).
- `npm run build` (from `frontend/`) — compiled successfully, no TypeScript errors.
- `npm run lint` (from `frontend/`) — no lint errors in any of the four changed files; all reported errors/warnings are pre-existing issues in unrelated files (verified via `npm run lint 2>&1 | grep -E "ManufacturingStockAnalysis|PurchaseStockAnalysis"` returning no output).
- `CI=true npx react-scripts test --watchAll=false grid-layout` — 3 suites / 23 tests passed (`useGridLayout.test.ts`, `GridHeader.test.tsx`, `ColumnChooser.test.tsx`).
- `CI=true npx react-scripts test --watchAll=false ManufacturingStockAnalysis PurchaseStockAnalysis` — 3 suites / 49 tests passed, confirming the two page components and their updated mocks still render and behave correctly.

## How to verify
1. `cd frontend && npm run build` — should compile with no type errors.
2. `cd frontend && npm run lint` — no errors attributable to `ManufacturingStockAnalysis.tsx`, `PurchaseStockAnalysis.tsx`, or their test files.
3. `cd frontend && CI=true npx react-scripts test --watchAll=false grid-layout` — all 3 suites green.
4. `cd frontend && CI=true npx react-scripts test --watchAll=false ManufacturingStockAnalysis PurchaseStockAnalysis` — all 3 suites green.
5. Manually: resize a column header in the Manufacturing or Purchase stock analysis grids — width should update live while dragging (no network calls per pixel) and persist (single debounced save) on mouseup.

## Notes
- The task context only listed the two page component files as files to modify, but grepping surfaced two additional test files (`ManufacturingStockAnalysis.test.tsx`, `PurchaseStockAnalysis.test.tsx`) that mock `useGridLayout` and still stubbed the removed `setColumnWidth`. These were updated to keep the mock shape aligned with the real hook and to satisfy the task's own verification grep (`setColumnWidth\b`), which would otherwise have flagged these two remaining literal matches. This is a small addition beyond the literal task-context diff but was necessary to fully meet the task's own stated verification criteria.
- Followed the pipeline orchestrator's commit message instruction (`feat(feat-3519): wire GridHeader onResizeChange/onResizeEnd to setColumnWidthLive/commitColumnWidth in consumers`) rather than the task context's suggested message, since the outer execution rules took precedence.
- `git add -A` picked up an unrelated pipeline-managed file, `artifacts/feat-3519/state.json`, which was already modified in the working tree before this task started; included per the exact instructed commit command.

## PR Summary
Wires `ManufacturingStockAnalysis.tsx` and `PurchaseStockAnalysis.tsx` to the new split `useGridLayout`/`GridHeader` contract from the two prior tasks, replacing the removed `setColumnWidth` with `onResizeChange={setColumnWidthLive}` (continuous, no save) and `onResizeEnd={commitColumnWidth}` (once, debounced save). Also updates the two pages' test mocks of `useGridLayout` to match the new return shape. Frontend build, lint, and all grid-layout plus page-level tests (72 tests across 6 suites) pass.

## Status
DONE
