import React from 'react';
import {
  DndContext,
  closestCenter,
  KeyboardSensor,
  PointerSensor,
  useSensor,
  useSensors,
  DragEndEvent,
} from '@dnd-kit/core';
import {
  arrayMove,
  SortableContext,
  sortableKeyboardCoordinates,
  rectSortingStrategy,
} from '@dnd-kit/sortable';
import DashboardTile from './DashboardTile';
import { DashboardTile as DashboardTileType } from '../../api/hooks/useDashboard';

interface DashboardGridProps {
  tiles: DashboardTileType[];
  onReorder?: (tileIds: string[]) => void;
  className?: string;
}

const DashboardGrid: React.FC<DashboardGridProps> = ({
  tiles,
  onReorder,
  className = ''
}) => {
  const sensors = useSensors(
    useSensor(PointerSensor),
    useSensor(KeyboardSensor, {
      coordinateGetter: sortableKeyboardCoordinates,
    })
  );

  const handleDragEnd = (event: DragEndEvent) => {
    const { active, over } = event;

    if (over && active.id !== over.id) {
      const oldIndex = tiles.findIndex(t => t.tileId === active.id);
      const newIndex = tiles.findIndex(t => t.tileId === over.id);

      const newTiles = arrayMove(tiles, oldIndex, newIndex);
      const tileIds = newTiles.map(t => t.tileId);

      onReorder?.(tileIds);
    }
  };

  if (tiles.length === 0) {
    return (
      <div className="flex items-center justify-center h-64 bg-gray-50 rounded-lg border-2 border-dashed border-gray-300">
        <div className="text-center">
          <div className="text-gray-400 text-lg mb-2">📊</div>
          <p className="text-gray-600 font-medium">Žádné dlaždice k zobrazení</p>
          <p className="text-gray-500 text-sm">Přidejte dlaždice v nastavení</p>
        </div>
      </div>
    );
  }

  return (
    <DndContext
      sensors={sensors}
      collisionDetection={closestCenter}
      onDragEnd={handleDragEnd}
    >
      <SortableContext items={tiles.map(t => t.tileId)} strategy={rectSortingStrategy}>
        <div
          className={`
            grid gap-4 w-full
            grid-cols-1 sm:grid-cols-2 md:grid-cols-3 lg:grid-cols-6
            auto-rows-fr
            ${className}
          `}
          style={{ minHeight: '200px' }}
          data-testid="dashboard-grid"
        >
          {tiles.map((tile) => (
            <DashboardTile
              key={tile.tileId}
              tile={tile}
            />
          ))}
        </div>
      </SortableContext>
    </DndContext>
  );
};

export default DashboardGrid;