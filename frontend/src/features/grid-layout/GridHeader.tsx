import React, { useRef } from 'react';
import {
  DndContext,
  DragEndEvent,
  PointerSensor,
  useSensor,
  useSensors,
} from '@dnd-kit/core';
import {
  SortableContext,
  horizontalListSortingStrategy,
  useSortable,
  arrayMove,
} from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import { ChevronDown, ChevronUp, GripVertical } from 'lucide-react';
import { GridColumn, GridColumnState } from './types';

interface SortableHeaderCellProps<TRow> {
  column: GridColumn<TRow>;
  state: GridColumnState;
  activeSortKey?: string;
  sortDescending?: boolean;
  onSort?: (sortKey: string) => void;
  onResizeEnd?: (id: string, newWidth: number) => void;
}

function SortableHeaderCell<TRow>({
  column,
  state,
  activeSortKey,
  sortDescending,
  onSort,
  onResizeEnd,
}: SortableHeaderCellProps<TRow>) {
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } =
    useSortable({ id: column.id, disabled: column.canReorder === false });

  const resizeStartX = useRef<number | null>(null);
  const resizeStartWidth = useRef<number>(state.width ?? column.defaultWidth ?? 100);

  const isActive = column.sortBy !== undefined && activeSortKey === column.sortBy;
  const isAscending = isActive && !sortDescending;
  const isDescending = isActive && sortDescending;

  const width = state.width ?? column.defaultWidth;
  const minWidth = column.minWidth ?? 60;

  const style: React.CSSProperties = {
    transform: CSS.Transform.toString(transform),
    transition,
    opacity: isDragging ? 0.5 : undefined,
    width: width ? `${width}px` : undefined,
    minWidth: `${minWidth}px`,
    position: 'relative',
  };

  const alignClass =
    column.align === 'right'
      ? 'text-right'
      : column.align === 'center'
        ? 'text-center'
        : 'text-left';

  const handleMouseDownResize = (e: React.MouseEvent) => {
    e.preventDefault();
    e.stopPropagation();
    resizeStartX.current = e.clientX;
    resizeStartWidth.current = state.width ?? column.defaultWidth ?? 100;

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
    window.addEventListener('mousemove', onMouseMove);
    window.addEventListener('mouseup', onMouseUp);
  };

  return (
    <th
      ref={setNodeRef}
      style={style}
      scope="col"
      className={`px-4 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider select-none ${alignClass} ${column.headerClassName ?? ''}`}
      onClick={() => column.sortBy && onSort?.(column.sortBy)}
    >
      <div className={`flex items-center gap-1 ${column.align === 'right' ? 'justify-end' : ''}`}>
        {column.canReorder !== false && (
          <span
            {...attributes}
            {...listeners}
            className="text-gray-300 hover:text-gray-500 cursor-grab active:cursor-grabbing flex-shrink-0"
            onClick={(e) => e.stopPropagation()}
          >
            <GripVertical className="h-3 w-3" />
          </span>
        )}
        <span className={column.sortBy ? 'cursor-pointer hover:text-gray-700' : ''}>
          {column.header}
        </span>
        {column.sortBy && (
          <div className="flex flex-col flex-shrink-0">
            <ChevronUp className={`h-3 w-3 ${isAscending ? 'text-indigo-600' : 'text-gray-300'}`} />
            <ChevronDown className={`h-3 w-3 -mt-1 ${isDescending ? 'text-indigo-600' : 'text-gray-300'}`} />
          </div>
        )}
      </div>
      {column.canResize !== false && (
        <div
          className="absolute right-0 top-0 h-full w-1.5 cursor-col-resize hover:bg-indigo-200"
          onMouseDown={handleMouseDownResize}
          onClick={(e) => e.stopPropagation()}
        />
      )}
    </th>
  );
}

interface GridHeaderProps<TRow> {
  columns: GridColumn<TRow>[];
  columnState: GridColumnState[];
  activeSortKey?: string;
  sortDescending?: boolean;
  onSort?: (sortKey: string) => void;
  onReorder?: (newOrderIds: string[]) => void;
  onResizeEnd?: (id: string, newWidth: number) => void;
}

export function GridHeader<TRow>({
  columns,
  columnState,
  activeSortKey,
  sortDescending,
  onSort,
  onReorder,
  onResizeEnd,
}: GridHeaderProps<TRow>) {
  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 5 } }));

  const visibleIds = columns.map((c) => c.id);

  const handleDragEnd = (event: DragEndEvent) => {
    const { active, over } = event;
    if (!over || active.id === over.id) return;

    const oldIndex = visibleIds.indexOf(active.id as string);
    const newIndex = visibleIds.indexOf(over.id as string);
    const newOrder = arrayMove(visibleIds, oldIndex, newIndex);
    onReorder?.(newOrder);
  };

  return (
    <DndContext sensors={sensors} onDragEnd={handleDragEnd}>
      <SortableContext items={visibleIds} strategy={horizontalListSortingStrategy}>
        <thead className="bg-gray-50 sticky top-0 z-10">
          <tr>
            {columns.map((col) => {
              const state = columnState.find((s) => s.id === col.id) ?? {
                id: col.id,
                order: 0,
                hidden: false,
                width: col.defaultWidth,
              };
              return (
                <SortableHeaderCell
                  key={col.id}
                  column={col}
                  state={state}
                  activeSortKey={activeSortKey}
                  sortDescending={sortDescending}
                  onSort={onSort}
                  onResizeEnd={onResizeEnd}
                />
              );
            })}
          </tr>
        </thead>
      </SortableContext>
    </DndContext>
  );
}
