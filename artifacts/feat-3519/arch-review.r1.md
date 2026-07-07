# Architecture Review: GridLayout — correct `onResizeEnd` firing semantics

## Skip Design: true

No new or changed UI. The visible behavior (live width preview during drag, snap on release, debounced persistence) is preserved exactly. This is an internal callback/prop-contract fix: split one overloaded callback into a continuous `onResizeChange` and an end-only `onResizeEnd`, and split `useGridLayout.setColumnWidth` into a live updater and a committing updater. No design work required.

## Architectural Fit Assessment

The change fits cleanly into existing conventions and touches only the `frontend/src/features/grid-layout` feature plus its two consumers. Verified facts:

- **`GridHeader.tsx`** — `SortableHeaderCellProps<TRow>` (line 19) and `GridHeaderProps<TRow>` (line 126) both currently carry a single `onResizeEnd?: (id: string, newWidth: number) => void`. `GridHeader` forwards it to each `SortableHeaderCell` (line 179). The `mousemove` handler (line 71–76) calls `onResizeEnd?.(column.id, newWidth)` on every move; `onMouseUp` (line 77–81) only tears down listeners and invokes nothing.
- **`useGridLayout.ts`** — `setColumnWidth` (line 122–134) guards on `canResize === false`, updates `columnState` via `setColumnState`, and calls `scheduleSave(next)` inside the updater. `scheduleSave` (line 93–106) is a 500 ms (`DEBOUNCE_MS`) debounced `gridLayouts_Save`. The hook returns `setColumnWidth` at line 172. This mirrors the shape of the sibling mutators `toggleColumnVisibility` and `setColumnOrder`.
- **Consumers** — Both destructure `setColumnWidth` from `useGridLayout` and pass `onResizeEnd={setColumnWidth}`:
  - `ManufacturingStockAnalysis.tsx:482` (destructure) and `:1432` (JSX prop)
  - `PurchaseStockAnalysis.tsx:484` (destructure) and `:971` (JSX prop)
- **Public surface** — `index.ts` exports the hook/components and types only, not individual return fields, so renaming/splitting the hook's returned functions is **not** a public-API break. It is an internal refactor with two in-repo call sites.
- **Tests** — `__tests__/useGridLayout.test.ts` already covers debounce behavior via `jest.useFakeTimers()` and `toggleColumnVisibility`; the new hook functions should be tested the same way. `GridHeader` has no existing test file.

The proposed approach aligns with the established mutator pattern. No architectural tension.

## Proposed Architecture

### Component Overview

```
SortableHeaderCell (GridHeader.tsx)
  mousedown → capture resizeStartX, resizeStartWidth
  mousemove(ev) → newWidth = clamp(startWidth + dx)  → onResizeChange(id, newWidth)   ── live, N times
  mouseup(ev)   → finalWidth = clamp(startWidth + dx) → onResizeEnd(id, finalWidth)    ── once, then teardown
        │                                    │
        ▼ onResizeChange                     ▼ onResizeEnd
GridHeader (pass-through, both props)
        │                                    │
        ▼                                    ▼
useGridLayout
  setColumnWidthLive(id, w)            commitColumnWidth(id, w)
    canResize guard                      canResize guard
    setColumnState (no save)            setColumnState + scheduleSave(next)  → debounced gridLayouts_Save
```

### Key Design Decisions

#### Decision 1: Two callbacks (Option A), not move-to-mouseup (Option B)

**Options considered:** (A) add `onResizeChange` for live preview + `onResizeEnd` for commit; (B) remove the `mousemove` call entirely and only fire on release (width snaps on release, no live preview).

**Chosen approach:** Option A, as the spec mandates (FR-1).

**Rationale:** Live preview is current, shipped behavior; the spec's NFR-1 and FR-4 acceptance criteria explicitly require "no visual regression." Option B would silently remove live resize. Option A makes the contract honest while preserving behavior. The prop split also gives future callers a truthful `onResizeEnd` for commit-on-release semantics — the original defect the brief identified.

#### Decision 2: Compute final width in `mouseup` from the event, not from a captured ref

**Options considered:** (a) store the last computed width in a `useRef` during `mousemove` and read it in `mouseup`; (b) recompute `finalWidth` directly in the `mouseup` handler from `ev.clientX` using the same `resizeStartX`/`resizeStartWidth`/`minWidth` closure.

**Chosen approach:** (b) — the `mouseup` `MouseEvent` carries `clientX`; compute `dx = ev.clientX - resizeStartX.current` and `finalWidth = Math.max(minWidth, resizeStartWidth.current + dx)`, identical to the `mousemove` formula.

**Rationale:** The current `onMouseUp` takes no argument and the `newWidth` in `onMouseMove` is a local const not visible to `onMouseUp` — so the naive "just move the call" fails to compile without a shared value. Recomputing from the event avoids introducing an extra ref, keeps a single source of truth for the width formula, and naturally satisfies FR-2's "start width if the pointer never moved" (dx = 0 → startWidth). Guard the computation with the existing `resizeStartX.current === null` early-return so a stray `mouseup` cannot fire `onResizeEnd` spuriously; clear the ref **after** computing/invoking, per FR-1 ("before the resize refs are cleared").

#### Decision 3: Replace `setColumnWidth` with two functions; do not keep a compat alias

**Options considered:** (a) keep `setColumnWidth` and add the two new functions; (b) replace `setColumnWidth` with `setColumnWidthLive` + `commitColumnWidth`.

**Chosen approach:** (b) replace, matching spec FR-3 / "API Design."

**Rationale:** Only two in-repo consumers exist and both are updated in the same change (FR-4). `setColumnWidth` is not part of the exported public surface. Keeping a dead alias invites the exact contract confusion this task removes. Both new functions retain the `canResize === false` guard and reuse `scheduleSave` unchanged.

## Implementation Guidance

### Directory / Module Structure

No new files required. Edit in place:

- `frontend/src/features/grid-layout/GridHeader.tsx` — add `onResizeChange` to both prop interfaces, forward it in `GridHeader`, wire `mousemove → onResizeChange` and `mouseup → onResizeEnd`.
- `frontend/src/features/grid-layout/useGridLayout.ts` — replace `setColumnWidth` with `setColumnWidthLive` and `commitColumnWidth`; update the returned object.
- `frontend/src/components/pages/ManufacturingStockAnalysis.tsx` — update destructure (`:482`) and JSX (`:1432`).
- `frontend/src/components/pages/PurchaseStockAnalysis.tsx` — update destructure (`:484`) and JSX (`:971`).

Recommended test additions:

- `frontend/src/features/grid-layout/__tests__/useGridLayout.test.ts` — add: `setColumnWidthLive` updates width and does **not** call `gridLayouts_Save` (assert after `advanceTimersByTime`); `commitColumnWidth` updates width **and** calls `gridLayouts_Save` once after debounce; both no-op for a `canResize:false` column. Note: `mockColumns` in the existing test has no `canResize:false` entry — add one (e.g. a fourth column, or set `canResize:false` on an existing one) to cover the guard.
- Optional but recommended: a new `frontend/src/features/grid-layout/__tests__/GridHeader.test.tsx` driving a `mousedown` → several `mousemove` → `mouseup` sequence via `window.dispatchEvent(new MouseEvent(...))`, asserting `onResizeChange` fires N times and `onResizeEnd` fires exactly once with the final clamped width. This is the direct regression guard for the reported defect.

### Interfaces and Contracts

```ts
// GridHeader.tsx — both SortableHeaderCellProps<TRow> and GridHeaderProps<TRow>
onResizeChange?: (id: string, newWidth: number) => void; // fires per mousemove (live)
onResizeEnd?:    (id: string, newWidth: number) => void; // fires once on mouseup (commit)
```

```ts
// useGridLayout return (replaces setColumnWidth)
setColumnWidthLive: (id: string, width: number) => void; // setColumnState only, no scheduleSave
commitColumnWidth:  (id: string, width: number) => void; // setColumnState + scheduleSave(next)
```

Contract invariants developers must hold:
- Both `GridHeader` props are optional; omitting either must not throw (matches current `onResizeEnd?.` optional-call style).
- `onResizeChange` receives `(column.id, newWidth)` — the exact tuple currently passed at line 75.
- `onResizeEnd` fires **exactly once** per gesture, after computing the final width and **before** clearing `resizeStartX.current`.
- Both hook functions preserve the `canResize === false` early return.
- The whole gesture yields **one** debounced `gridLayouts_Save`, because only `commitColumnWidth` calls `scheduleSave`.

### Data Flow

1. `mousedown` on the resize handle → capture `resizeStartX` and `resizeStartWidth` (unchanged).
2. Each `mousemove` → compute clamped `newWidth` → `onResizeChange(id, newWidth)` → `setColumnWidthLive` → `setColumnState` re-renders the grid with the new width; **no save scheduled**.
3. `mouseup` → recompute clamped `finalWidth` from `ev.clientX` → `onResizeEnd(id, finalWidth)` → `commitColumnWidth` → `setColumnState` + `scheduleSave(next)` → after 500 ms, one `gridLayouts_Save`. Then remove listeners and null `resizeStartX`.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `mouseup` handler can't see `mousemove`'s local `newWidth`; naive "move the call" won't compile | Medium | Recompute `finalWidth` from `ev.clientX` in `mouseup` (Decision 2); reuse the identical clamp formula. |
| Stray/duplicate `mouseup` fires `onResizeEnd` twice | Low | Keep the `resizeStartX.current === null` guard at the top of `mouseup`; clear the ref only after invoking. |
| Missed consumer call site leaves a dangling `setColumnWidth` reference (build break) | Low | Only two sites exist (`ManufacturingStockAnalysis.tsx:482,1432`; `PurchaseStockAnalysis.tsx:484,971`); `npm run build` (tsc) will flag any missed one. |
| Existing debounce test relies on `toggleColumnVisibility`; new functions untested | Low | Add hook tests mirroring the existing fake-timer pattern (see Test additions). |
| Consumer passes `commitColumnWidth` to `onResizeEnd` but forgets `onResizeChange` → live preview lost | Medium | FR-4 acceptance requires both props wired; the optional GridHeader test asserts the N-vs-1 firing split as a regression guard. |

## Specification Amendments

- **FR-2 clarification (compilation reality):** The spec says the `mouseup` handler must "compute (or capture) the final width." Direct computation from the `mouseup` `MouseEvent.clientX` is required, not merely preferred — the existing `onMouseUp` takes no argument and cannot read `onMouseMove`'s local `newWidth`. State this explicitly so implementers don't attempt a non-compiling literal move of the call.
- **Test-fixture gap:** `useGridLayout.test.ts`'s `mockColumns` contains no `canResize:false` column. To satisfy FR-3's "no-op when `canResize === false`" acceptance criterion, the fixture must gain such a column. Add this to the spec's test expectations.
- **No other amendments.** Data model, API/HTTP surface, `index.ts` exports, and out-of-scope boundaries are correct as written.

## Prerequisites

None. No migrations, config, feature flags, infrastructure, or backend changes. The change is confined to four existing frontend files plus test additions, buildable and testable with the standard `npm run build` / `npm run lint` / Jest workflow.
