### task: wire-consumers

**Files:**
- Modify: `frontend/src/components/pages/ManufacturingStockAnalysis.tsx` (destructure at lines 482–483; JSX at line 1432)
- Modify: `frontend/src/components/pages/PurchaseStockAnalysis.tsx` (destructure at lines 484–485; JSX at line 971)

- [ ] Update the `ManufacturingStockAnalysis` destructure. In `frontend/src/components/pages/ManufacturingStockAnalysis.tsx`, replace lines 482–483:

  ```tsx
    const { orderedColumns, columnState, setColumnOrder, setColumnWidth, toggleColumnVisibility, resetLayout } =
      useGridLayout('manufacturing-stock-analysis', columns);
  ```

  with:

  ```tsx
    const { orderedColumns, columnState, setColumnOrder, setColumnWidthLive, commitColumnWidth, toggleColumnVisibility, resetLayout } =
      useGridLayout('manufacturing-stock-analysis', columns);
  ```

- [ ] Update the `ManufacturingStockAnalysis` `GridHeader` JSX. In the same file, replace line 1432 (`onResizeEnd={setColumnWidth}`) with:

  ```tsx
                onResizeChange={setColumnWidthLive}
                onResizeEnd={commitColumnWidth}
  ```

- [ ] Update the `PurchaseStockAnalysis` destructure. In `frontend/src/components/pages/PurchaseStockAnalysis.tsx`, replace lines 484–485:

  ```tsx
    const { orderedColumns, columnState, setColumnOrder, setColumnWidth, toggleColumnVisibility, resetLayout } =
      useGridLayout('purchase-stock-analysis', purchaseStockColumns);
  ```

  with:

  ```tsx
    const { orderedColumns, columnState, setColumnOrder, setColumnWidthLive, commitColumnWidth, toggleColumnVisibility, resetLayout } =
      useGridLayout('purchase-stock-analysis', purchaseStockColumns);
  ```

- [ ] Update the `PurchaseStockAnalysis` `GridHeader` JSX. In the same file, replace line 971 (`onResizeEnd={setColumnWidth}`) with:

  ```tsx
                onResizeChange={setColumnWidthLive}
                onResizeEnd={commitColumnWidth}
  ```

- [ ] Verify no dangling `setColumnWidth` references remain. From the repo root:

  ```
  grep -rn "setColumnWidth\b" frontend/src
  ```

  Expected: no matches (only `setColumnWidthLive` / `commitColumnWidth` remain).

- [ ] Run the full frontend build to confirm the TypeScript compiles with the new contract. From `frontend/`:

  ```
  npm run build
  ```

  Expected: build succeeds with no type errors (any missed call site would fail `tsc`).

- [ ] Run lint. From `frontend/`:

  ```
  npm run lint
  ```

  Expected: no new lint errors in the four changed files or the two new/updated test files.

- [ ] Run the grid-layout tests together to confirm the whole feature is green. From `frontend/`:

  ```
  CI=true npx react-scripts test --watchAll=false grid-layout
  ```

  Expected: `useGridLayout.test.ts`, `GridHeader.test.tsx`, and `ColumnChooser.test.tsx` all pass.

- [ ] Commit. From the repo root:

  ```
  git add frontend/src/components/pages/ManufacturingStockAnalysis.tsx frontend/src/components/pages/PurchaseStockAnalysis.tsx
  git commit -m "Wire stock-analysis pages to split grid resize callbacks"
  ```

---

## Self-review: requirement coverage

- **FR-1** (split into `onResizeChange` + `onResizeEnd` on both prop interfaces; `GridHeader` passes both through) → `task: split-gridheader-resize-callbacks` (both interfaces edited, pass-through added, `GridHeader.test.tsx` asserts the split).
- **FR-2** (`onResizeEnd` fires once on release with final clamped width, recomputed from `ev.clientX`, before refs cleared, guarded against stray mouseup) → `task: split-gridheader-resize-callbacks` (new `onMouseUp(ev)` with `resizeStartX.current === null` guard, computes `finalWidth`, invokes before nulling ref; N-vs-1 and clamp tests).
- **FR-3** (`useGridLayout` splits into `setColumnWidthLive` (no save) and `commitColumnWidth` (state + `scheduleSave`), both keep `canResize === false` guard) → `task: split-hook-updaters` (implementation + four tests including both no-op guards and single-save assertion).
- **FR-4** (both consumers destructure the new functions and pass `onResizeChange`/`onResizeEnd`) → `task: wire-consumers` (both pages updated; `grep` guard; `npm run build` catches misses).
- **NFR-1** (live preview retained, save at most once per gesture, no extra renders) → `setColumnWidthLive` keeps the same per-`mousemove` `setColumnState` (no render increase); only `commitColumnWidth` calls `scheduleSave`; asserted by `commitColumnWidth ... exactly one debounced save` test.
- **NFR-2** (no security/endpoint change) → no API surface touched; persistence still via existing `scheduleSave` → `gridLayouts_Save`.
- **Data model / `index.ts` exports** → unchanged; barrel exports the hook/components, not return fields, so no export edit is needed.

Type/name consistency verified: prop type `(id: string, newWidth: number) => void` identical across `SortableHeaderCellProps`, `GridHeaderProps`, hook signatures, and consumer wiring; function names `setColumnWidthLive` / `commitColumnWidth` used identically in hook return, tests, and both consumers.
