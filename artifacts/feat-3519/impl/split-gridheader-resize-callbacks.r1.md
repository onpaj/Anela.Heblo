# Implementation: split-gridheader-resize-callbacks

## What was implemented
Split `GridHeader`'s single resize callback into two: `onResizeChange` (fires on every `mousemove` during a column-resize drag, for live preview) and `onResizeEnd` (fires exactly once, on `mouseup`, with the final clamped width). Previously `onResizeEnd` was misnamed — it actually fired continuously on every `mousemove`. Both `SortableHeaderCellProps` and `GridHeaderProps` gained the new `onResizeChange` prop, and `GridHeader` forwards it through to each `SortableHeaderCell`.

## Files created/modified
- `frontend/src/features/grid-layout/GridHeader.tsx` — added `onResizeChange?: (id: string, newWidth: number) => void` to `SortableHeaderCellProps` and `GridHeaderProps`; destructured it in both `SortableHeaderCell` and `GridHeader`; rewired `handleMouseDownResize` so `onMouseMove` calls `onResizeChange` (unclamped-tracking-then-clamped `newWidth`) and `onMouseUp` computes the final clamped width and calls `onResizeEnd` exactly once before tearing down the listeners; forwarded `onResizeChange` in the `SortableHeaderCell` JSX next to `onResizeEnd`.
- `frontend/src/features/grid-layout/__tests__/GridHeader.test.tsx` (new) — regression tests for the split behavior.

## Tests
`frontend/src/features/grid-layout/__tests__/GridHeader.test.tsx`:
- `fires onResizeChange per mousemove and onResizeEnd exactly once on mouseup` — drags via 3 mousemoves then a mouseup; asserts `onResizeChange` called 3 times (last with the live width), `onResizeEnd` not called until mouseup, then called exactly once with the final width, and no extra `onResizeChange` call fires on mouseup itself.
- `clamps the final width to minWidth when dragged below it` — drags past `minWidth` and asserts `onResizeEnd` receives the clamped value.
- `does not throw when both resize callbacks are omitted` — renders `GridHeader` without either prop and drags through mousedown/mousemove/mouseup, asserting no throw (optional-chaining guards work).

## How to verify
```
cd frontend
CI=true npx react-scripts test --watchAll=false GridHeader.test.tsx
```
Result: 3/3 tests pass (1 test suite). Confirmed the tests failed for the expected reason before the implementation change (0 calls to `onResizeChange` since the prop didn't exist; `onResizeEnd` assertions failed because it fired on every mousemove instead of once on mouseup), and passed after.

## Notes
- File matched the task context's described line numbers/content exactly (no drift), so all edits were applied verbatim as specified.
- As expected per the task context, this branch's existing consumers (`ManufacturingStockAnalysis.tsx`, `PurchaseStockAnalysis.tsx`) still reference the removed `setColumnWidth` from `useGridLayout` (per the prior `split-hook-updaters` commit) and were not touched here — they remain broken until the `wire-consumers` task rewires them to use `onResizeChange`/`onResizeEnd` with `setColumnWidthLive`/`commitColumnWidth`. Did not run a full `npm run build` for this reason, consistent with the prior task's note; the scoped `GridHeader.test.tsx` run is the correct verification for this task's scope.
- `git add -A` (per instructions) also picked up an unrelated pipeline-bookkeeping change to `artifacts/feat-3519/state.json` that was already present/modified in the worktree; included in the commit per the explicit `git add -A` instruction given for this task.

## PR Summary
`GridHeader`'s resize handling previously called a prop named `onResizeEnd` on every `mousemove`, which was both misleadingly named and meant every pointer-move during a column drag triggered a full save. This change splits it into `onResizeChange` (continuous, for live width preview) and a true `onResizeEnd` that fires exactly once on `mouseup` with the final clamped width — matching the semantics the earlier `split-hook-updaters` task's `setColumnWidthLive`/`commitColumnWidth` pair expects from its caller. `GridHeaderProps` and `SortableHeaderCellProps` both gained the new prop and forward it through unchanged otherwise.

### Changes
- `frontend/src/features/grid-layout/GridHeader.tsx`
- `frontend/src/features/grid-layout/__tests__/GridHeader.test.tsx`

## Status
DONE
