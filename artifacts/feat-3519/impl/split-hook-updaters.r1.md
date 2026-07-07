# Implementation: split-hook-updaters

## What was implemented
Split the single `setColumnWidth` updater in `useGridLayout` into two distinct mutators:
- `setColumnWidthLive(id, width)` — updates `columnState` immediately with no debounced save (for live resize preview, e.g. `onResize`).
- `commitColumnWidth(id, width)` — updates `columnState` and schedules exactly one debounced save (for the final value, e.g. `onResizeEnd`).

Both preserve the original `canResize === false` no-op guard. The hook's returned object now exposes `setColumnWidthLive` and `commitColumnWidth` instead of `setColumnWidth`.

## Files created/modified
- `frontend/src/features/grid-layout/useGridLayout.ts` — replaced the `setColumnWidth` callback with `setColumnWidthLive` (no `scheduleSave`, empty dep array) and `commitColumnWidth` (calls `scheduleSave`, same behavior as the old `setColumnWidth`); updated the hook's return object accordingly.
- `frontend/src/features/grid-layout/__tests__/useGridLayout.test.ts` — added a `canResize:false` `locked` column to `mockColumns`; updated array-equality assertions that enumerate all column ids to include `locked` (four assertions total — two were called out in the task context, two more were discovered while running the tests, see Notes); appended a new `describe('useGridLayout — width mutators', ...)` block with four tests for the new mutators.

## Tests
`frontend/src/features/grid-layout/__tests__/useGridLayout.test.ts`:
- `setColumnWidthLive updates width without scheduling a save` — verifies width updates synchronously and no save fires even after the debounce window elapses.
- `commitColumnWidth updates width and schedules exactly one debounced save` — verifies width updates synchronously, no save before the debounce window, exactly one `gridLayouts_Save` call after.
- `setColumnWidthLive no-ops for a canResize:false column` — verifies the `locked` column's width is unchanged.
- `commitColumnWidth no-ops for a canResize:false column` — verifies the `locked` column's width is unchanged and no save is scheduled.

Also verified the pre-existing merge-behavior and DB-error-preservation tests still pass with the new 4-column fixture.

## How to verify
```
cd frontend
CI=true npx react-scripts test --watchAll=false useGridLayout.test.ts
```
Result: 14/14 tests pass (1 test suite).

Note: `node_modules` was not present in this worktree; it was populated from the sibling checkout at `/home/user/Anela.Heblo/frontend/node_modules` (identical `package-lock.json`) purely to run the test command locally — this is not part of the commit.

## Notes
- The task context said the `applies saved order`, `appends new columns`, and `preserves existing columnState` tests "assert only subsets/indices and do not need changes." In practice, three additional full-array `toEqual` assertions in those tests (lines that originally read `.toEqual(['name', 'reserve', 'stock'])`, one in `applies saved order from backend` and two in `preserves existing columnState when a re-load fails`) broke once the `locked` column was added, because `locked` isn't present in any saved-layout fixture and gets appended at the end of the merged state. I updated those three assertions to append `'locked'`, preserving each test's original intent (order/hidden-state assertions), consistent with how the two explicitly-called-out lines (38 and 178 in the task context, i.e. the "no saved layout" and "load fails on first mount" tests) were changed.
- Scope was intentionally kept to only `useGridLayout.ts` and its test file, per the task context. `PurchaseStockAnalysis.tsx` and `ManufacturingStockAnalysis.tsx` still destructure the now-removed `setColumnWidth` and pass it to `onResizeEnd` — this will cause a TypeScript build error until the `wire-consumers` task (present alongside this one in `artifacts/feat-3519/task-context/`) updates those call sites to use `setColumnWidthLive`/`commitColumnWidth`. This is expected: the task context explicitly says "since a later task depends on these exact names," and a sibling task file `wire-consumers.md` exists for that follow-up work.
- Did not run `npm run build` / `npm run lint` for the whole frontend since the known-broken consumer files make a full build fail until `wire-consumers` lands; the scoped test run above is the correct verification for this task.
- `artifacts/feat-3519/state.json` had uncommitted changes not related to this task (pipeline bookkeeping) — left out of the commit intentionally.

## PR Summary
Splits `useGridLayout`'s single `setColumnWidth` mutator into `setColumnWidthLive` (immediate visual update, no save) and `commitColumnWidth` (visual update plus debounced save), so callers can drive live-resize previews without triggering saves on every pointer-move event and only persist once on resize-end. Test fixture gained a `canResize:false` "locked" column and four new tests cover both mutators' happy path and no-op-on-locked-column behavior.

### Changes
- `frontend/src/features/grid-layout/useGridLayout.ts`
- `frontend/src/features/grid-layout/__tests__/useGridLayout.test.ts`

## Status
DONE_WITH_CONCERNS
