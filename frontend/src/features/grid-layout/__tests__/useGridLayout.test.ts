import { renderHook, act, waitFor } from '@testing-library/react';
import { useGridLayout } from '../useGridLayout';
import { GridColumn } from '../types';
import { getAuthenticatedApiClient } from '../../../api/client';
import { GridLayoutDto, GridColumnStateDto } from '../../../api/generated/api-client';

jest.mock('../../../api/client');

const mockGridLayouts_Get = jest.fn();
const mockGridLayouts_Save = jest.fn();
const mockGridLayouts_Reset = jest.fn();

const mockClient = {
  gridLayouts_Get: mockGridLayouts_Get,
  gridLayouts_Save: mockGridLayouts_Save,
  gridLayouts_Reset: mockGridLayouts_Reset,
};

beforeEach(() => {
  jest.clearAllMocks();
  (getAuthenticatedApiClient as jest.Mock).mockReturnValue(mockClient);
  mockGridLayouts_Get.mockResolvedValue(null);
  mockGridLayouts_Save.mockResolvedValue(undefined);
  mockGridLayouts_Reset.mockResolvedValue(undefined);
});

const mockColumns: GridColumn<{ id: string }>[] = [
  { id: 'name', header: 'Name', canHide: false, canReorder: false, renderCell: (r) => r.id },
  { id: 'stock', header: 'Stock', defaultWidth: 100, renderCell: (r) => r.id },
  { id: 'reserve', header: 'Reserve', defaultWidth: 80, renderCell: (r) => r.id },
];

describe('useGridLayout — merge behavior', () => {
  it('uses default order/visibility when no saved layout', async () => {
    const { result } = renderHook(() => useGridLayout('test-grid', mockColumns));
    await waitFor(() => expect(result.current.isLoaded).toBe(true));

    expect(result.current.orderedColumns.map((c) => c.id)).toEqual(['name', 'stock', 'reserve']);
    expect(result.current.columnState.every((c) => !c.hidden)).toBe(true);
  });

  it('applies saved order from backend', async () => {
    const savedLayout = new GridLayoutDto();
    const col1 = new GridColumnStateDto();
    col1.id = 'name';
    col1.order = 0;
    col1.hidden = false;
    const col2 = new GridColumnStateDto();
    col2.id = 'reserve';
    col2.order = 1;
    col2.hidden = false;
    const col3 = new GridColumnStateDto();
    col3.id = 'stock';
    col3.order = 2;
    col3.hidden = false;
    savedLayout.columns = [col1, col2, col3];
    mockGridLayouts_Get.mockResolvedValue(savedLayout);

    const { result } = renderHook(() => useGridLayout('test-grid', mockColumns));
    await waitFor(() => expect(result.current.isLoaded).toBe(true));

    expect(result.current.orderedColumns.map((c) => c.id)).toEqual(['name', 'reserve', 'stock']);
  });

  it('appends new columns not in saved layout at the end', async () => {
    // Saved layout only knows 'name' and 'stock' — 'reserve' was added later in code
    const savedLayout = new GridLayoutDto();
    const col1 = new GridColumnStateDto();
    col1.id = 'name';
    col1.order = 0;
    col1.hidden = false;
    const col2 = new GridColumnStateDto();
    col2.id = 'stock';
    col2.order = 1;
    col2.hidden = false;
    savedLayout.columns = [col1, col2];
    mockGridLayouts_Get.mockResolvedValue(savedLayout);

    const { result } = renderHook(() => useGridLayout('test-grid', mockColumns));
    await waitFor(() => expect(result.current.isLoaded).toBe(true));

    expect(result.current.orderedColumns[2].id).toBe('reserve');
  });

  it('forces canHide:false column visible even if saved as hidden', async () => {
    const savedLayout = new GridLayoutDto();
    const col1 = new GridColumnStateDto();
    col1.id = 'name';
    col1.order = 0;
    col1.hidden = true;
    const col2 = new GridColumnStateDto();
    col2.id = 'stock';
    col2.order = 1;
    col2.hidden = false;
    const col3 = new GridColumnStateDto();
    col3.id = 'reserve';
    col3.order = 2;
    col3.hidden = false;
    savedLayout.columns = [col1, col2, col3];
    mockGridLayouts_Get.mockResolvedValue(savedLayout);

    const { result } = renderHook(() => useGridLayout('test-grid', mockColumns));
    await waitFor(() => expect(result.current.isLoaded).toBe(true));

    const nameState = result.current.columnState.find((c) => c.id === 'name');
    expect(nameState?.hidden).toBe(false);
  });
});

describe('useGridLayout — mutators', () => {
  beforeEach(() => {
    jest.useFakeTimers();
  });

  afterEach(() => {
    jest.useRealTimers();
  });

  it('toggleColumnVisibility hides a column immediately', async () => {
    const { result } = renderHook(() => useGridLayout('test-grid', mockColumns));
    await waitFor(() => expect(result.current.isLoaded).toBe(true));

    act(() => {
      result.current.toggleColumnVisibility('stock');
    });

    const stockState = result.current.columnState.find((c) => c.id === 'stock');
    expect(stockState?.hidden).toBe(true);
  });

  it('toggleColumnVisibility does nothing for canHide:false columns', async () => {
    const { result } = renderHook(() => useGridLayout('test-grid', mockColumns));
    await waitFor(() => expect(result.current.isLoaded).toBe(true));

    act(() => {
      result.current.toggleColumnVisibility('name');
    });

    const nameState = result.current.columnState.find((c) => c.id === 'name');
    expect(nameState?.hidden).toBe(false);
  });

  it('debounces save — does not call gridLayouts_Save immediately', async () => {
    const { result } = renderHook(() => useGridLayout('test-grid', mockColumns));
    await waitFor(() => expect(result.current.isLoaded).toBe(true));

    act(() => {
      result.current.toggleColumnVisibility('stock');
    });

    expect(mockGridLayouts_Save).not.toHaveBeenCalled();
  });

  it('calls gridLayouts_Save after debounce delay', async () => {
    const { result } = renderHook(() => useGridLayout('test-grid', mockColumns));
    await waitFor(() => expect(result.current.isLoaded).toBe(true));

    act(() => {
      result.current.toggleColumnVisibility('stock');
    });
    act(() => {
      jest.advanceTimersByTime(600);
    });

    await waitFor(() =>
      expect(mockGridLayouts_Save).toHaveBeenCalledWith('test-grid', expect.anything()),
    );
  });
});

describe('useGridLayout — DB-error preservation', () => {
  it('falls back to default state when initial load fails (first mount, empty state)', async () => {
    mockGridLayouts_Get.mockRejectedValueOnce(new Error('HTTP 500'));

    const { result } = renderHook(() => useGridLayout('test-grid', mockColumns));
    await waitFor(() => expect(result.current.isLoaded).toBe(true));

    expect(result.current.columnState.map((c) => c.id)).toEqual(['name', 'stock', 'reserve']);
    expect(result.current.columnState.every((c) => !c.hidden)).toBe(true);
  });

  it('preserves existing columnState when a re-load fails (non-empty state)', async () => {
    // First mount: successful load with a custom saved layout.
    const savedLayout = new GridLayoutDto();
    const c1 = new GridColumnStateDto();
    c1.id = 'name';
    c1.order = 0;
    c1.hidden = false;
    const c2 = new GridColumnStateDto();
    c2.id = 'reserve';
    c2.order = 1;
    c2.hidden = false;
    const c3 = new GridColumnStateDto();
    c3.id = 'stock';
    c3.order = 2;
    c3.hidden = true; // user had hidden stock
    savedLayout.columns = [c1, c2, c3];
    mockGridLayouts_Get.mockResolvedValueOnce(savedLayout);

    const { result, rerender } = renderHook(
      ({ gridKey }) => useGridLayout(gridKey, mockColumns),
      { initialProps: { gridKey: 'grid-a' } },
    );
    await waitFor(() => expect(result.current.isLoaded).toBe(true));
    const stateBeforeReload = result.current.columnState;
    expect(stateBeforeReload.find((c) => c.id === 'stock')?.hidden).toBe(true);
    expect(stateBeforeReload.map((c) => c.id)).toEqual(['name', 'reserve', 'stock']);

    // Re-load triggered by gridKey change: backend returns non-2xx.
    mockGridLayouts_Get.mockRejectedValueOnce(new Error('HTTP 500'));
    rerender({ gridKey: 'grid-b' });
    // Wait for the hook to complete processing the error and updating state
    // The test should FAIL here because the current implementation resets to default state
    await waitFor(
      () => {
        // The API should have been called
        expect(mockGridLayouts_Get).toHaveBeenCalledWith('grid-b');
      },
      { timeout: 1000 },
    );

    // Add explicit wait for state to settle after async operations
    await act(async () => {
      await new Promise(r => setTimeout(r, 100));
    });

    // columnState must be preserved — user's hidden 'stock' column must still be hidden,
    // and the user's order ['name', 'reserve', 'stock'] must remain.
    expect(result.current.columnState.find((c) => c.id === 'stock')?.hidden).toBe(true);
    expect(result.current.columnState.map((c) => c.id)).toEqual(['name', 'reserve', 'stock']);
  });
});
