# [arch-review] GridLayouts: onResizeEnd fires on every mousemove, not on drag completion

## Module
GridLayouts

## Finding
In `frontend/src/features/grid-layout/GridHeader.tsx`, the `onResizeEnd` prop is called inside `onMouseMove` — on every pixel of cursor movement during a column resize — not only when the mouse button is released:

```typescript
// GridHeader.tsx lines 71–77
const onMouseMove = (ev: MouseEvent) => {
  if (resizeStartX.current === null) return;
  const dx = ev.clientX - resizeStartX.current;
  const newWidth = Math.max(minWidth, resizeStartWidth.current + dx);
  onResizeEnd?.(column.id, newWidth);   // ← fires on every mousemove
};
const onMouseUp = () => {
  resizeStartX.current = null;
  window.removeEventListener('mousemove', onMouseMove);
  window.removeEventListener('mouseup', onMouseUp);
};
```

The prop's name (`onResizeEnd`) and its type `(id: string, newWidth: number) => void` signal "called once when the resize gesture completes." In practice it fires continuously. Every call propagates to `useGridLayout.setColumnWidth`, which calls `setColumnState(...)` — a React state update — triggering a full re-render of the grid on every pixel of mouse movement.

The 500 ms debounce in `scheduleSave` prevents excessive *server* calls, but the local re-renders are unguarded.

## Why it matters
For grids with many rows, this causes dozens of re-renders per second during a resize, making the interaction janky. More importantly, the misleading prop name creates a contract mismatch: any future caller that reads the signature and expects end-only semantics (e.g. to commit a draft width) will behave incorrectly.

## Suggested fix
Two minimal options:

**Option A — rename the prop to match actual behavior:**
```typescript
// GridHeaderProps
onResizeChange?: (id: string, newWidth: number) => void;  // fires continuously
onResizeEnd?:    (id: string, newWidth: number) => void;  // fires on mouseup only
```
Wire `onResizeChange` to the `mousemove` handler and `onResizeEnd` to `onMouseUp`. `useGridLayout` connects to `onResizeChange` for live state and `onResizeEnd` for debounced save.

**Option B — keep the prop but move the call to `onMouseUp`:**
Remove the call from `onMouseMove` and add it to `onMouseUp`. Accept that the column width snaps on release rather than updating live. Simpler, no re-render churn.

Option A is preferred if live resize preview is a product requirement; Option B is the minimal fix if it is not.

---
_Filed by daily arch-review routine on 2026-07-06._
