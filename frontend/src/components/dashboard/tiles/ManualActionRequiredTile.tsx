import React from 'react';
import { AlertTriangle } from 'lucide-react';
import { CountTile } from './CountTile';

interface ManualActionRequiredTileProps {
  data: {
    status?: string;
    data?: {
      count?: number;
      date?: string;
    };
    error?: string;
  };
}

export const ManualActionRequiredTile: React.FC<ManualActionRequiredTileProps> = ({ data }) => {
  // Extract count from data
  const count = data.data?.count ?? 0;
  
  // Determine icon color based on count
  // count = 0: Success (green) - all is OK
  // count > 0: Danger (red) - requires attention
  const iconColor = count === 0 ? 'text-emerald-600' : 'text-red-600';
  
  return (
    <CountTile
      data={data}
      icon={<AlertTriangle className="h-8 w-8" />}
      iconColor={iconColor}
    />
  );
};

export default ManualActionRequiredTile;