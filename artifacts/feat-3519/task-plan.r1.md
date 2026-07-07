# GridLayouts onResizeEnd Fix — Implementation Plan

**Goal:** Make `GridHeader`'s `onResizeEnd` fire exactly once on mouse release (not on every `mousemove`) by splitting it into a live `onResizeChange` preview callback and an end-only `onResizeEnd` commit callback, wired to split `useGridLayout` updaters so a resize produces one debounced save.

**Architecture:** In `GridHeader.tsx` add an `onResizeChange` prop to both `SortableHeaderCellProps` and `GridHeaderProps`; the `mousemove` handler calls `onResizeChange` (live), and the `mouseup` handler recomputes the final clamped width from `ev.clientX` and calls `onResizeEnd` exactly once before clearing `resizeStartX.current`. In `useGridLayout.ts` replace the single `setColumnWidth` with `setColumnWidthLive` (state only, no save) and `commitColumnWidth` (state + `scheduleSave`), both preserving the `canResize === false` guard. The two consumer pages pass `onResizeChange={setColumnWidthLive}` and `onResizeEnd={commitColumnWidth}`. No public `index.ts` export changes, no backend/schema changes.

**Tech Stack:** React + TypeScript, `@dnd-kit/*` (unaffected), Jest + `@testing-library/react` (`renderHook`, fake timers), react-scripts test runner.

---

### task: split-hook-updaters

**Files:**
- Modify: `frontend/src/features/grid-layout/useGridLayout.ts` (replace `setColumnWidth` at lines 122–134; update return object at line 172)
- Test: `frontend/src/features/grid-layout/__tests__/useGridLayout.test.ts` (extend `mockColumns` at lines 27–31; add a new `describe` block for the width mutators)

- [ ] Add a `canResize:false` column to the test fixture. In `frontend/src/features/grid-layout/__tests__/useGridLayout.test.ts`, replace the `mockColumns` array (lines 27–31):

  ```ts
  const mockColumns: GridColumn<{ id: string }>[] = [
    { id: 'name', header: 'Name', canHide: false, canReorder: false, renderCell: (r) => r.id },
    { id: 'stock', header: 'Stock', defaultWidth: 100, renderCell: (r) => r.id },
    { id: 'reserve', header: 'Reserve', defaultWidth: 80, renderCell: (r) => r.id },
    { id: 'locked', header: 'Locked', defaultWidth: 120, canResize: false, renderCell: (r) => r.id },
  ];
  ```

- [ ] Update the existing merge-behavior assertions that hard-code the column id list so the new `locked` column does not break them. In the same file, change line 38 from `.toEqual(['name', 'stock', 'reserve'])` to `.toEqual(['name', 'stock', 'reserve', 'locked'])`, and change line 178 from `.toEqual(['name', 'stock', 'reserve'])` to `.toEqual(['name', 'stock', 'reserve', 'locked'])`. (The `applies saved order`, `appends new columns`, and `preserves existing columnState` tests assert only subsets/indices and do not need changes.)

- [ ] Write the failing tests for the new mutators. Append this `describe` block to the end of `frontend/src/features/grid-layout/__tests__/useGridLayout.test.ts` (after the `DB-error preservation` block, after line 232):

  ```ts
  describe('useGridLayout — width mutators', () => {
    beforeEach(() => {
      jest.useFakeTimers();
    });

    afterEach(() => {
      jest.useRealTimers();
    });

    it('setColumnWidthLive updates width without scheduling a save', async () => {
      const { result } = renderHook(() => useGridLayout('test-grid', mockColumns));
      await waitFor(() => expect(result.current.isLoaded).toBe(true));

      act(() => {
        result.current.setColumnWidthLive('stock', 250);
      });

      expect(result.current.columnState.find((c) => c.id === 'stock')?.width).toBe(250);

      act(() => {
        jest.advanceTimersByTime(600);
      });
      expect(mockGridLayouts_Save).not.toHaveBeenCalled();
    });

    it('commitColumnWidth updates width and schedules exactly one debounced save', async () => {
      const { result } = renderHook(() => useGridLayout('test-grid', mockColumns));
      await waitFor(() => expect(result.current.isLoaded).toBe(true));

      act(() => {
        result.current.commitColumnWidth('stock', 300);
      });

      expect(result.current.columnState.find((c) => c.id === 'stock')?.width).toBe(300);
      expect(mockGridLayouts_Save).not.toHaveBeenCalled();

      act(() => {
        jest.advanceTimersByTime(600);
      });

      await waitFor(() =>
        expect(mockGridLayouts_Save).toHaveBeenCalledWith('test-grid', expect.anything()),
      );
      expect(mockGridLayouts_Save).toHaveBeenCalledTimes(1);
    });

    it('setColumnWidthLive no-ops for a canResize:false column', async () => {
      const { result } = renderHook(() => useGridLayout('test-grid', mockColumns));
      await waitFor(() => expect(result.current.isLoaded).toBe(true));

      act(() => {
        result.current.setColumnWidthLive('locked', 999);
      });

      expect(result.current.columnState.find((c) => c.id === 'locked')?.width).toBe(120);
    });

    it('commitColumnWidth no-ops for a canResize:false column', async () => {
      const { result } = renderHook(() => useGridLayout('test-grid', mockColumns));
      await waitFor(() => expect(result.current.isLoaded).toBe(true));

      act(() => {
        result.current.commitColumnWidth('locked', 999);
      });

      expect(result.current.columnState.find((c) => c.id === 'locked')?.width).toBe(120);

      act(() => {
        jest.advanceTimersByTime(600);
      });
      expect(mockGridLayouts_Save).not.toHaveBeenCalled();
    });
  });
  ```

- [ ] Run the test to confirm it fails (the new mutators do not exist yet). From `frontend/`:

  ```
  CI=true npx react-scripts test --watchAll=false useGridLayout.test.ts
  ```

  Expected: the four new `width mutators` tests fail with `TypeError: result.current.setColumnWidthLive is not a function` / `...commitColumnWidth is not a function`. The updated merge-behavior tests (with `locked`) pass.

- [ ] Implement the two mutators. In `frontend/src/features/grid-layout/useGridLayout.ts`, replace the `setColumnWidth` block (lines 122–134):

  ```ts
  const setColumnWidth = useCallback(
    (id: string, width: number) => {
      const col = columnsRef.current.find((c) => c.id === id);
      if (col?.canResize === false) return;

      setColumnState((prev) => {
        const next = prev.map((s) => (s.id === id ? { ...s, width } : s));
        scheduleSave(next);
        return next;
      });
    },
    [scheduleSave],
  );
  ```

  with:

  ```ts
  const setColumnWidthLive = useCallback(
    (id: string, width: number) => {
      const col = columnsRef.current.find((c) => c.id === id);
      if (col?.canResize === false) return;

      setColumnState((prev) => prev.map((s) => (s.id === id ? { ...s, width } : s)));
    },
    [],
  );

  const commitColumnWidth = useCallback(
    (id: string, width: number) => {
      const col = columnsRef.current.find((c) => c.id === id);
      if (col?.canResize === false) return;

      setColumnState((prev) => {
        const next = prev.map((s) => (s.id === id ? { ...s, width } : s));
        scheduleSave(next);
        return next;
      });
    },
    [scheduleSave],
  );
  ```

- [ ] Update the hook's returned object. In `frontend/src/features/grid-layout/useGridLayout.ts`, in the `return { ... }` block (lines 168–176), replace the line `setColumnWidth,` (line 172) with:

  ```ts
    setColumnWidthLive,
    commitColumnWidth,
  ```

- [ ] Run the tests to confirm they pass. From `frontend/`:

  ```
  CI=true npx react-scripts test --watchAll=false useGridLayout.test.ts
  ```

  Expected: all tests in the file pass (merge behavior, mutators, DB-error preservation, and the four new width mutators).

- [ ] Commit. From the repo root:

  ```
  git add frontend/src/features/grid-layout/useGridLayout.ts frontend/src/features/grid-layout/__tests__/useGridLayout.test.ts
  git commit -m "Split useGridLayout width mutator into live and commit updaters"
  ```

---

### task: split-gridheader-resize-callbacks

**Files:**
- Modify: `frontend/src/features/grid-layout/GridHeader.tsx` (prop interfaces at lines 19–26 and 126–134; destructure at 28–35 and 136–144; resize handlers at 71–81; pass-through at 172–180)
- Test: `frontend/src/features/grid-layout/__tests__/GridHeader.test.tsx` (new file)

- [ ] Write the failing regression test. Create `frontend/src/features/grid-layout/__tests__/GridHeader.test.tsx`:

  ```tsx
  import { render, fireEvent } from '@testing-library/react';
  import { GridHeader } from '../GridHeader';
  import { GridColumn, GridColumnState } from '../types';

  const columns: GridColumn<{ id: string }>[] = [
    { id: 'stock', header: 'Stock', defaultWidth: 100, minWidth: 60, renderCell: (r) => r.id },
  ];

  const columnState: GridColumnState[] = [{ id: 'stock', order: 0, width: 100, hidden: false }];

  function renderHeader(
    onResizeChange: (id: string, w: number) => void,
    onResizeEnd: (id: string, w: number) => void,
  ) {
    return render(
      <table>
        <GridHeader
          columns={columns}
          columnState={columnState}
          onResizeChange={onResizeChange}
          onResizeEnd={onResizeEnd}
        />
      </table>,
    );
  }

  it('fires onResizeChange per mousemove and onResizeEnd exactly once on mouseup', () => {
    const onResizeChange = jest.fn();
    const onResizeEnd = jest.fn();
    const { container } = renderHeader(onResizeChange, onResizeEnd);

    const handle = container.querySelector('.cursor-col-resize') as HTMLElement;
    expect(handle).toBeTruthy();

    fireEvent.mouseDown(handle, { clientX: 200 });
    fireEvent.mouseMove(window, { clientX: 210 });
    fireEvent.mouseMove(window, { clientX: 230 });
    fireEvent.mouseMove(window, { clientX: 260 });

    expect(onResizeChange).toHaveBeenCalledTimes(3);
    expect(onResizeChange).toHaveBeenLastCalledWith('stock', 160); // 100 + (260 - 200)
    expect(onResizeEnd).not.toHaveBeenCalled();

    fireEvent.mouseUp(window, { clientX: 260 });

    expect(onResizeEnd).toHaveBeenCalledTimes(1);
    expect(onResizeEnd).toHaveBeenCalledWith('stock', 160);
    expect(onResizeChange).toHaveBeenCalledTimes(3); // no extra change on mouseup
  });

  it('clamps the final width to minWidth when dragged below it', () => {
    const onResizeChange = jest.fn();
    const onResizeEnd = jest.fn();
    const { container } = renderHeader(onResizeChange, onResizeEnd);

    const handle = container.querySelector('.cursor-col-resize') as HTMLElement;
    fireEvent.mouseDown(handle, { clientX: 200 });
    fireEvent.mouseUp(window, { clientX: 50 }); // dx = -150 → 100 - 150 = -50, clamp to 60

    expect(onResizeEnd).toHaveBeenCalledWith('stock', 60);
  });

  it('does not throw when both resize callbacks are omitted', () => {
    const { container } = render(
      <table>
        <GridHeader columns={columns} columnState={columnState} />
      </table>,
    );
    const handle = container.querySelector('.cursor-col-resize') as HTMLElement;
    expect(() => {
      fireEvent.mouseDown(handle, { clientX: 200 });
      fireEvent.mouseMove(window, { clientX: 230 });
      fireEvent.mouseUp(window, { clientX: 230 });
    }).not.toThrow();
  });
  ```

- [ ] Run the test to confirm it fails. From `frontend/`:

  ```
  CI=true npx react-scripts test --watchAll=false GridHeader.test.tsx
  ```

  Expected: the first two tests fail — `onResizeChange` is not a known prop (TypeScript would also flag it) and `onResizeEnd` is currently called on every `mousemove` (so `onResizeEnd` is called 3 times before mouseup, and `onResizeChange` is never called). The third (omitted-callbacks) test passes.

- [ ] Add `onResizeChange` to `SortableHeaderCellProps`. In `frontend/src/features/grid-layout/GridHeader.tsx`, replace the interface (lines 19–26):

  ```ts
  interface SortableHeaderCellProps<TRow> {
    column: GridColumn<TRow>;
    state: GridColumnState;
    activeSortKey?: string;
    sortDescending?: boolean;
    onSort?: (sortKey: string) => void;
    onResizeChange?: (id: string, newWidth: number) => void;
    onResizeEnd?: (id: string, newWidth: number) => void;
  }
  ```

- [ ] Destructure `onResizeChange` in `SortableHeaderCell`. In the same file, replace the destructure params (lines 28–35):

  ```ts
  function SortableHeaderCell<TRow>({
    column,
    state,
    activeSortKey,
    sortDescending,
    onSort,
    onResizeChange,
    onResizeEnd,
  }: SortableHeaderCellProps<TRow>) {
  ```

- [ ] Rewire the resize handlers so `mousemove` calls `onResizeChange` and `mouseup` computes the final width and calls `onResizeEnd` once. In the same file, replace the `onMouseMove`/`onMouseUp` block (lines 71–81):

  ```ts
    const onMouseMove = (ev: MouseEvent) => {
      if (resizeStartX.current === null) return;
      const dx = ev.clientX - resizeStartX.current;
      const newWidth = Math.max(minWidth, resizeStartWidth.current + dx);
      onResizeEnd?.(column.id, newWidth);
    };
    const onMouseUp = () => {
      resizeStartX.current = null;
      window.removeEventListener('mousemove', onMouseMove);
      window.removeEventListener('mouseup', onMouseUp);
    };
  ```

  with:

  ```ts
    const onMouseMove = (ev: MouseEvent) => {
      if (resizeStartX.current === null) return;
      const dx = ev.clientX - resizeStartX.current;
      const newWidth = Math.max(minWidth, resizeStartWidth.current + dx);
      onResizeChange?.(column.id, newWidth);
    };
    const onMouseUp = (ev: MouseEvent) => {
      if (resizeStartX.current === null) return;
      const dx = ev.clientX - resizeStartX.current;
      const finalWidth = Math.max(minWidth, resizeStartWidth.current + dx);
      onResizeEnd?.(column.id, finalWidth);
      resizeStartX.current = null;
      window.removeEventListener('mousemove', onMouseMove);
      window.removeEventListener('mouseup', onMouseUp);
    };
  ```

- [ ] Add `onResizeChange` to `GridHeaderProps`. In the same file, replace the interface (lines 126–134):

  ```ts
  interface GridHeaderProps<TRow> {
    columns: GridColumn<TRow>[];
    columnState: GridColumnState[];
    activeSortKey?: string;
    sortDescending?: boolean;
    onSort?: (sortKey: string) => void;
    onReorder?: (newOrderIds: string[]) => void;
    onResizeChange?: (id: string, newWidth: number) => void;
    onResizeEnd?: (id: string, newWidth: number) => void;
  }
  ```

- [ ] Destructure `onResizeChange` in `GridHeader`. In the same file, replace the destructure params (lines 136–144):

  ```ts
  export function GridHeader<TRow>({
    columns,
    columnState,
    activeSortKey,
    sortDescending,
    onSort,
    onReorder,
    onResizeChange,
    onResizeEnd,
  }: GridHeaderProps<TRow>) {
  ```

- [ ] Forward `onResizeChange` to each `SortableHeaderCell`. In the same file, in the `SortableHeaderCell` JSX (lines 172–180), add the pass-through prop next to `onResizeEnd` (line 179):

  ```tsx
                <SortableHeaderCell
                  key={col.id}
                  column={col}
                  state={state}
                  activeSortKey={activeSortKey}
                  sortDescending={sortDescending}
                  onSort={onSort}
                  onResizeChange={onResizeChange}
                  onResizeEnd={onResizeEnd}
                />
  ```

- [ ] Run the test to confirm it passes. From `frontend/`:

  ```
  CI=true npx react-scripts test --watchAll=false GridHeader.test.tsx
  ```

  Expected: all three tests pass — `onResizeChange` called 3 times, `onResizeEnd` called once with the final clamped width, clamp-to-`minWidth` on release, and no throw when callbacks are omitted.

- [ ] Commit. From the repo root:

  ```
  git add frontend/src/features/grid-layout/GridHeader.tsx frontend/src/features/grid-layout/__tests__/GridHeader.test.tsx
  git commit -m "Split GridHeader resize into onResizeChange (live) and onResizeEnd (once)"
  ```

---

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
