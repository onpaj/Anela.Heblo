import { useCallback, useEffect, useRef, useState } from 'react';
import { GridColumnStateDto, SaveGridLayoutRequest } from '../../api/generated/api-client';
import { getAuthenticatedApiClient } from '../../api/client';
import { GridColumn, GridColumnState } from './types';

const DEBOUNCE_MS = 500;

function buildDefaultState<TRow>(columns: GridColumn<TRow>[]): GridColumnState[] {
  return columns.map((col, index) => ({
    id: col.id,
    order: index,
    width: col.defaultWidth,
    hidden: false,
  }));
}

function mergeStates<TRow>(
  columns: GridColumn<TRow>[],
  saved: GridColumnStateDto[],
): GridColumnState[] {
  const savedMap = new Map(saved.map((s) => [s.id ?? '', s]));

  const merged: GridColumnState[] = columns.map((col, fallbackOrder) => {
    const s = savedMap.get(col.id);
    const hidden = col.canHide === false ? false : (s?.hidden ?? false);
    return {
      id: col.id,
      order: s?.order ?? fallbackOrder,
      width: s?.width ?? col.defaultWidth,
      hidden,
    };
  });

  merged.sort((a, b) => a.order - b.order);

  // Re-number order sequentially to close gaps
  return merged.map((s, i) => ({ ...s, order: i }));
}

function toSaveRequest(state: GridColumnState[]): SaveGridLayoutRequest {
  const req = new SaveGridLayoutRequest();
  req.columns = state.map((s) => {
    const dto = new GridColumnStateDto();
    dto.id = s.id;
    dto.order = s.order;
    dto.width = s.width;
    dto.hidden = s.hidden;
    return dto;
  });
  return req;
}

export function useGridLayout<TRow>(gridKey: string, columns: GridColumn<TRow>[]) {
  const [columnState, setColumnState] = useState<GridColumnState[]>([]);
  const [isLoaded, setIsLoaded] = useState(false);
  const debounceTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const columnsRef = useRef(columns);

  useEffect(() => {
    columnsRef.current = columns;
  }, [columns]);

  // Load saved layout from backend on mount
  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const apiClient = getAuthenticatedApiClient();
        const layout = await apiClient.gridLayouts_Get(gridKey);
        if (cancelled) return;

        const state =
          layout?.columns && layout.columns.length > 0
            ? mergeStates(columnsRef.current, layout.columns)
            : buildDefaultState(columnsRef.current);

        setColumnState(state);
      } catch (error) {
        console.warn('Failed to load grid layout:', error);
        if (!cancelled) setColumnState(buildDefaultState(columnsRef.current));
      } finally {
        if (!cancelled) setIsLoaded(true);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [gridKey]); // eslint-disable-line react-hooks/exhaustive-deps

  const scheduleSave = useCallback(
    (nextState: GridColumnState[]) => {
      if (debounceTimer.current) clearTimeout(debounceTimer.current);
      debounceTimer.current = setTimeout(async () => {
        try {
          const apiClient = getAuthenticatedApiClient();
          await apiClient.gridLayouts_Save(gridKey, toSaveRequest(nextState));
        } catch {
          // Silently ignore save errors — visual state is already updated
        }
      }, DEBOUNCE_MS);
    },
    [gridKey],
  );

  const toggleColumnVisibility = useCallback(
    (id: string) => {
      const col = columnsRef.current.find((c) => c.id === id);
      if (col?.canHide === false) return;

      setColumnState((prev) => {
        const next = prev.map((s) => (s.id === id ? { ...s, hidden: !s.hidden } : s));
        scheduleSave(next);
        return next;
      });
    },
    [scheduleSave],
  );

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

  const setColumnOrder = useCallback(
    (newOrderIds: string[]) => {
      setColumnState((prev) => {
        const next = newOrderIds.map((id, index) => {
          const existing = prev.find((s) => s.id === id);
          return existing ? { ...existing, order: index } : { id, hidden: false, order: index };
        });
        scheduleSave(next);
        return next;
      });
    },
    [scheduleSave],
  );

  const resetLayout = useCallback(async () => {
    if (debounceTimer.current) clearTimeout(debounceTimer.current);
    try {
      const apiClient = getAuthenticatedApiClient();
      await apiClient.gridLayouts_Reset(gridKey);
    } catch {
      // ignore
    }
    setColumnState(buildDefaultState(columnsRef.current));
  }, [gridKey]);

  // Derived: visible + ordered columns
  const orderedColumns = columnState
    .filter((s) => !s.hidden)
    .sort((a, b) => a.order - b.order)
    .map((s) => columnsRef.current.find((c) => c.id === s.id))
    .filter((col): col is GridColumn<TRow> => col !== undefined);

  return {
    orderedColumns,
    columnState,
    setColumnOrder,
    setColumnWidth,
    toggleColumnVisibility,
    resetLayout,
    isLoaded,
  };
}
