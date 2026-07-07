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
