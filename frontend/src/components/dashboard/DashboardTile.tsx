import React from 'react';
import { useSortable } from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import { DashboardTile as DashboardTileType } from '../../api/hooks/useDashboard';
import { TileHeader, TileContent } from './tiles';

interface DashboardTileProps {
  tile: DashboardTileType;
  className?: string;
}

const DashboardTile: React.FC<DashboardTileProps> = ({
  tile,
  className = ''
}) => {
  const {
    attributes,
    listeners,
    setNodeRef,
    transform,
    transition,
    isDragging,
  } = useSortable({ id: tile.tileId });

  const style = {
    transform: CSS.Transform.toString(transform),
    transition,
    opacity: isDragging ? 0.5 : 1,
  };

  const getSizeClasses = () => {
    switch (tile.size) {
      case 'Small':
        return 'col-span-1 row-span-1';
      case 'Medium':
        return 'col-span-2 row-span-1';
      case 'Large':
        return 'col-span-2 row-span-2';
      default:
        return 'col-span-1 row-span-1';
    }
  };

  return (
    <div
      ref={setNodeRef}
      style={style}
      className={`
        bg-white rounded-lg shadow-sm border border-gray-200
        hover:shadow-md transition-shadow duration-200
        flex flex-col
        ${getSizeClasses()}
        ${className}
      `}
      data-testid={`dashboard-tile-${tile.tileId}`}
    >
      <TileHeader
        title={tile.title}
        dragHandleProps={{ ...attributes, ...listeners }}
      />

      <div className="p-4 flex-1">
        <TileContent tile={tile} />
      </div>
    </div>
  );
};

export default DashboardTile;
