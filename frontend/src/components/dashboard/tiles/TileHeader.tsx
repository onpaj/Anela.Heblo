import React from 'react';
import { GripVertical } from 'lucide-react';

interface TileHeaderProps {
  title: string;
  dragHandleProps?: any;
}

export const TileHeader: React.FC<TileHeaderProps> = ({ title, dragHandleProps }) => {
  return (
    <div className="px-4 py-3 border-b border-gray-100 flex justify-between items-center">
      <h3 className="text-sm font-medium text-gray-900 truncate tile-title">
        {title}
      </h3>
      <button
        {...dragHandleProps}
        className="text-gray-400 hover:text-gray-600 transition-colors cursor-grab active:cursor-grabbing"
        title="Přetáhnout pro změnu pořadí"
      >
        <GripVertical className="h-4 w-4" />
      </button>
    </div>
  );
};
