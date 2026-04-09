import React from 'react';

export type ColumnAlign = 'left' | 'right' | 'center';

export interface GridColumn<TRow> {
  /** Stable key used in persisted state. Never change once deployed. */
  id: string;
  header: React.ReactNode;
  defaultWidth?: number; // px; undefined = no explicit width
  minWidth?: number; // px; default 60
  align?: ColumnAlign;
  /** Sort key passed to the page's onSort callback. Omit for unsortable columns. */
  sortBy?: string;
  /** If false, column cannot be toggled in the chooser and is always visible. Default: true */
  canHide?: boolean;
  /** If false, column cannot be dragged to a new position. Default: true */
  canReorder?: boolean;
  /** If false, no resize handle is rendered. Default: true */
  canResize?: boolean;
  renderCell: (row: TRow) => React.ReactNode;
  /** Extra Tailwind classes for the <th> */
  headerClassName?: string;
  /** Extra Tailwind classes for each <td> in this column */
  cellClassName?: string;
}

export interface GridColumnState {
  id: string;
  order: number;
  width?: number; // px; undefined = use column defaultWidth
  hidden: boolean;
}

export interface GridLayoutPayload {
  columns: GridColumnState[];
}
