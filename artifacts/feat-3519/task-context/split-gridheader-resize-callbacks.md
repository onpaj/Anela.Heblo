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
