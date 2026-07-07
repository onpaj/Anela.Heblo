# Specification: GridLayout — correct `onResizeEnd` firing semantics

## Summary
The reusable grid-layout header (`GridHeader.tsx`) exposes an `onResizeEnd` prop whose name and type signal "called once when a column-resize gesture completes," but the callback is actually invoked on every `mousemove` during the drag. This spec defines a fix that makes the prop contract honest: a continuously-firing preview callback drives the live width, and an end-only callback drives the debounced persistence. The change is confined to the `frontend/src/features/grid-layout` feature and its two current consumers.

## Background
`GridHeader.tsx` renders resizable column headers. On mouse-down of a resize handle it registers `window` `mousemove`/`mouseup` listeners. Today the `mousemove` handler calls `onResizeEnd?.(column.id, newWidth)` on every pixel of movement (`GridHeader.tsx:71–76`), and `mouseup` only tears the listeners down without invoking any callback.

Both current consumers — `ManufacturingStockAnalysis.tsx:1432` and `PurchaseStockAnalysis.tsx:971` — wire `onResizeEnd={setColumnWidth}` from `useGridLayout`. `setColumnWidth` calls `setColumnState(...)` (a React state update triggering a full grid re-render) and then `scheduleSave(...)` (a 500 ms debounced backend save). Because the callback fires on every `mousemove`:

1. **Contract mismatch.** The prop name `onResizeEnd` and type `(id: string, newWidth: number) => void` imply end-of-gesture semantics. Any future caller that trusts the name (e.g. to commit a draft width once) will behave incorrectly.
2. **Re-render churn.** For grids with many rows, each `mousemove` re-renders the whole grid, producing dozens of re-renders per second and a janky resize. The debounce guards only the *server* call, not local re-renders.

This was flagged by the daily arch-review routine (`artifacts/feat-3519/brief.md`, 2026-07-06).

## Functional Requirements

### FR-1: Split the resize callback into a live-change callback and an end-of-gesture callback
`SortableHeaderCellProps` and `GridHeaderProps` in `GridHeader.tsx` must expose two distinct, optional props:

- `onResizeChange?: (id: string, newWidth: number) => void` — fires continuously while the pointer moves during a resize (i.e. from the `mousemove` handler), for live width preview.
- `onResizeEnd?: (id: string, newWidth: number) => void` — fires exactly once per resize gesture, when the mouse button is released (from the `mouseup` handler), for persistence.

`GridHeader` must pass both props through to each `SortableHeaderCell`.

**Acceptance criteria:**
- Both prop types match `(id: string, newWidth: number) => void`.
- `onResizeChange` is invoked from the `mousemove` handler with the same `(column.id, newWidth)` currently passed.
- `onResizeEnd` is invoked from the `mouseup` handler with the final computed width for that gesture, before the resize refs are cleared.
- Both props are optional; omitting either does not throw.

### FR-2: `onResizeEnd` fires once, on release, with the final width
The `mouseup` handler must compute (or capture) the final width from the last pointer position and invoke `onResizeEnd?.(column.id, finalWidth)` exactly once, then remove the `mousemove`/`mouseup` listeners. `onResizeEnd` must not fire during pointer movement.

**Acceptance criteria:**
- Given a resize drag across N intermediate `mousemove` events followed by one `mouseup`, `onResizeChange` is called N times and `onResizeEnd` is called exactly once.
- The width passed to `onResizeEnd` equals the width from the final `mousemove` (or the start width if the pointer never moved), clamped to `minWidth`.
- No `onResizeEnd` call occurs if the resize handle is pressed and released without registration issues (the single release call is the only one).

### FR-3: `useGridLayout` separates live update from debounced save
`useGridLayout` must expose the update surface so that live pointer movement updates local column-width state **without** scheduling a save, and the end-of-gesture commits the final width **and** schedules the debounced backend save. Concretely, provide two functions (names indicative):

- `setColumnWidthLive(id, width)` — updates `columnState` only (no `scheduleSave`).
- `commitColumnWidth(id, width)` — updates `columnState` and calls `scheduleSave(next)`.

The existing `canResize === false` guard must be preserved in both.

**Acceptance criteria:**
- `setColumnWidthLive` updates the column's width in `columnState` and does **not** invoke the debounced save.
- `commitColumnWidth` updates the column's width and invokes `scheduleSave` exactly once.
- Both no-op when the target column has `canResize === false`.
- A resize gesture results in exactly one debounced save (a single `gridLayouts_Save` call after `DEBOUNCE_MS`), regardless of the number of intermediate `mousemove` events.

### FR-4: Wire consumers to the new contract
`ManufacturingStockAnalysis.tsx` and `PurchaseStockAnalysis.tsx` must destructure the new functions from `useGridLayout` and pass `onResizeChange={setColumnWidthLive}` and `onResizeEnd={commitColumnWidth}` to `GridHeader`.

**Acceptance criteria:**
- Both pages compile and pass `onResizeChange` and `onResizeEnd`.
- Live resize still visibly updates the column width during the drag (no visual regression from current behavior).
- Exactly one save request is issued per completed resize.

## Non-Functional Requirements

### NFR-1: Performance
Live width preview during a resize is retained, but the debounced backend save must be triggered at most once per completed gesture. There must be no increase in re-render frequency versus today; the change should not introduce additional renders. Re-render volume during drag is unchanged unless a later optimization is chosen (see Out of Scope), because live preview inherently updates state per `mousemove`.

### NFR-2: Security
No change. No new endpoints, no change to auth, no new data exposure. Persistence continues through the existing authenticated `gridLayouts_Save` / `gridLayouts_Get` / `gridLayouts_Reset` client methods.

## Data Model
No changes. `GridColumnState { id, order, width?, hidden }` and the persisted `SaveGridLayoutRequest` / `GridColumnStateDto` shapes are unchanged. Only the client-side callback wiring changes.

## API / Interface Design
No backend/HTTP changes. Component-level interface changes only:

- `GridHeaderProps<TRow>` and `SortableHeaderCellProps<TRow>` gain `onResizeChange?: (id: string, newWidth: number) => void` alongside the existing `onResizeEnd?`.
- `useGridLayout` returns two width functions (`setColumnWidthLive`, `commitColumnWidth`) in place of the single `setColumnWidth`. The `index.ts` public exports of the feature are unchanged (they export the hook/components, not individual return fields).

UI flow (unchanged behavior, corrected semantics):
1. User presses a column resize handle → `mousedown` captures start X and start width.
2. User drags → each `mousemove` fires `onResizeChange` → `setColumnWidthLive` updates local width live.
3. User releases → `mouseup` fires `onResizeEnd` once → `commitColumnWidth` updates final width and schedules one debounced save.

## Dependencies
- `useGridLayout` hook and its debounced-save mechanism (`scheduleSave`, `DEBOUNCE_MS = 500`).
- Generated API client methods `gridLayouts_Save` / `gridLayouts_Get` / `gridLayouts_Reset`.
- `@dnd-kit/*` for sortable headers (unaffected by this change).
- Consumers: `ManufacturingStockAnalysis.tsx`, `PurchaseStockAnalysis.tsx`.

## Out of Scope
- Reducing re-render count during drag below the current per-`mousemove` level (e.g. driving live width via a CSS variable / ref instead of React state). This spec preserves current live-preview behavior; a render-throttling optimization can be a separate task.
- Touch / pointer-event support beyond the existing mouse events.
- Any change to reorder, visibility toggle, sort, or reset behavior.
- Backend or persisted-schema changes.

## Open Questions
None.

## Status: COMPLETE
