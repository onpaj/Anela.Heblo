import React from 'react';
import { AlertTriangle, XCircle } from 'lucide-react';
import {
  DashboardTileDrillDown,
  resolveDrillDown,
} from '../drillDownRoutes';

interface FailedJobsTileProps {
  data: {
    status?: string;
    data?: {
      count?: number;
    };
    error?: string;
    drillDown?: DashboardTileDrillDown;
  };
}

export const FailedJobsTile: React.FC<FailedJobsTileProps> = ({ data }) => {
  const resolution = resolveDrillDown(data.drillDown);

  const handleClick = () => {
    if (!resolution) {
      return;
    }
    if (resolution.strategy === 'external') {
      window.open(resolution.url, '_blank');
    }
    // react-router strategy is not used by this tile
  };

  if (data.status === 'error') {
    return (
      <div className="h-full flex items-center justify-center text-center">
        <div>
          <XCircle className="h-10 w-10 text-red-500 dark:text-red-400 mx-auto mb-2" />
          <p className="text-red-600 dark:text-red-400 text-sm">Unavailable</p>
        </div>
      </div>
    );
  }

  const count = data.data?.count ?? 0;

  return (
    <div
      data-testid="failed-jobs-tile"
      className="flex flex-col items-center justify-center h-full leading-relaxed min-h-44 cursor-pointer hover:bg-gray-50 dark:hover:bg-white/5 active:bg-gray-100 dark:active:bg-white/10 transition-colors duration-200 rounded-lg"
      onClick={handleClick}
      style={{ touchAction: 'manipulation' }}
    >
      <div className="mb-2 text-red-600 dark:text-red-400">
        <AlertTriangle className="h-10 w-10" />
      </div>
      <div className="text-3xl font-bold mb-1 text-red-700 dark:text-red-400">
        {count}
      </div>
      <div className="text-sm text-gray-500 dark:text-graphite-muted">
        failed jobs
      </div>
    </div>
  );
};
