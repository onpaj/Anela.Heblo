# Design: GridLayout — correct `onResizeEnd` firing semantics

## Component Design

No new components. Two existing modules in `frontend/src/features/grid-layout` change their internal contracts; two consumer pages update their wiring to match. No public export names from the feature's `index.ts` change.

### `GridHeader.tsx`

- **`SortableHeaderCellProps<TRow>`** (currently line 19) and **`GridHeaderProps<TRow>`** (currently line 126) each gain a new optional prop, `onResizeChange`, alongside the existing `onResizeEnd` (whose meaning changes — see below):
  - `onResizeChange?: (id: string, newWidth: number) => void` — continuous, fires on every `mousemove` during an active resize drag. Responsible for live width preview only.
  - `onResizeEnd?: (id: string, newWidth: number) => void` — fires exactly once per resize gesture, from `mouseup`, with the final width. Responsible for committing/persisting the result.
- **`GridHeader`** forwards both props unchanged to each `SortableHeaderCell` (pass-through only, no logic).
- **`SortableHeaderCell`**'s resize handlers:
  - `mousemove` handler: computes clamped `newWidth` from `resizeStartX`/`resizeStartWidth`/`minWidth` (unchanged formula) and calls `onResizeChange?.(column.id, newWidth)` instead of the current `onResizeEnd?.(...)`.
  - `mouseup` handler: recomputes the clamped final width directly from the `MouseEvent.clientX` using the same formula (`dx = ev.clientX - resizeStartX.current`, `finalWidth = Math.max(minWidth, resizeStartWidth.current + dx)`), guarded by the existing `resizeStartX.current === null` early-return so a stray `mouseup` cannot fire spuriously. It calls `onResizeEnd?.(column.id, finalWidth)` exactly once, then tears down the `mousemove`/`mouseup` listeners and clears `resizeStartX.current` (invocation happens before the ref is cleared).
- Both props remain optional; omitting either must not throw (preserves the existing `?.` optional-call style).

### `useGridLayout.ts`

- The single mutator `setColumnWidth` (currently lines 122–134) is replaced by two functions with the same `canResize === false` guard and the same `setColumnState` update mechanics:
  - `setColumnWidthLive(id: string, width: number): void` — updates `columnState` only. Does **not** call `scheduleSave`.
  - `commitColumnWidth(id: string, width: number): void` — updates `columnState` and calls `scheduleSave(next)`, identical to today's `setColumnWidth` body.
- No compatibility alias is kept for `setColumnWidth` — it has exactly two in-repo call sites, both updated in this change, and it is not part of the feature's public surface (`index.ts` exports the hook itself, not individual return fields).
- The hook's returned object drops `setColumnWidth` and adds `setColumnWidthLive` and `commitColumnWidth` in its place. All other returned fields/functions (`toggleColumnVisibility`, `setColumnOrder`, `scheduleSave`, etc.) are unchanged.

### Consumers

- `ManufacturingStockAnalysis.tsx` and `PurchaseStockAnalysis.tsx` destructure `setColumnWidthLive` and `commitColumnWidth` from `useGridLayout` (replacing the single `setColumnWidth` destructure) and pass them to `GridHeader` as:
  - `onResizeChange={setColumnWidthLive}`
  - `onResizeEnd={commitColumnWidth}`

### Data flow (for reference)

1. `mousedown` on a resize handle → capture `resizeStartX`, `resizeStartWidth` (unchanged).
2. Each `mousemove` → clamped `newWidth` computed → `onResizeChange(id, newWidth)` → `setColumnWidthLive` → `setColumnState` re-renders with the live width; no save scheduled.
3. `mouseup` → clamped `finalWidth` recomputed from the event → `onResizeEnd(id, finalWidth)` → `commitColumnWidth` → `setColumnState` + `scheduleSave(next)` → after `DEBOUNCE_MS` (500 ms), one `gridLayouts_Save` call. Listeners removed and `resizeStartX.current` nulled afterward.

## Data Schemas

No database, API, or wire-format changes. This is a client-side, in-process callback/prop-contract split only.

- `GridColumnState { id, order, width?, hidden }` — unchanged.
- `SaveGridLayoutRequest` / `GridColumnStateDto` (persisted shapes) — unchanged.
- Generated API client methods `gridLayouts_Save` / `gridLayouts_Get` / `gridLayouts_Reset` — unchanged, called exactly as today (once per debounce window, now guaranteed to be exactly once per completed resize gesture rather than the current one-call-per-`mousemove` mismatch that the debounce was masking).

### New TypeScript interface shapes (component-level contracts only)

```ts
// GridHeader.tsx — both SortableHeaderCellProps<TRow> and GridHeaderProps<TRow>
onResizeChange?: (id: string, newWidth: number) => void; // fires per mousemove (live preview)
onResizeEnd?:    (id: string, newWidth: number) => void; // fires once on mouseup (commit)
```

```ts
// useGridLayout return shape (replaces setColumnWidth)
setColumnWidthLive: (id: string, width: number) => void; // setColumnState only, no scheduleSave
commitColumnWidth:  (id: string, width: number) => void; // setColumnState + scheduleSave(next)
```

Invariants:
- `onResizeChange` receives `(column.id, newWidth)` on every `mousemove`, matching the tuple currently passed to the (mis-firing) `onResizeEnd`.
- `onResizeEnd` fires exactly once per gesture, with the final clamped width, before `resizeStartX.current` is cleared.
- Both hook functions preserve the `canResize === false` early-return (no-op).
- Exactly one debounced `gridLayouts_Save` occurs per completed resize gesture, since only `commitColumnWidth` calls `scheduleSave`.
