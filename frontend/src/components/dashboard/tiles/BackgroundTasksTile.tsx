import React from 'react';
import { useNavigate } from 'react-router-dom';
import { 
  createFilteredUrl,
  isTileClickable, 
  getTileTooltip, 
  TileDataWithDrillDown 
} from '../../../utils/urlUtils';

interface BackgroundTasksTileProps {
  data: TileDataWithDrillDown & {
    data?: {
      Completed?: number;
      Total?: number;
      completed?: number;  // camelCase fallback
      total?: number;      // camelCase fallback
    };
  };
}

export const BackgroundTasksTile: React.FC<BackgroundTasksTileProps> = ({ data }) => {
  const navigate = useNavigate();
  // Try both PascalCase and camelCase versions due to JSON serialization
  const tileData = data.data || {};
  const completed = tileData.Completed ?? tileData.completed ?? 0;
  const total = tileData.Total ?? tileData.total ?? 0;
  
  // Debug: log the actual data structure
  console.log('BackgroundTasksTile data:', { data, tileData, completed, total });
  
  const isClickable = isTileClickable(data);
  const tooltip = getTileTooltip(data);

  const handleClick = () => {
    if (isClickable && data.drillDown?.filters !== undefined) {
      // Background tasks should navigate to automation/background-tasks page
      const url = createFilteredUrl('/automation/background-tasks', data.drillDown.filters);
      navigate(url);
    }
  };

  return (
    <div 
      className={`
        flex flex-col items-center justify-center
        ${isClickable ? 'cursor-pointer hover:bg-gray-50 transition-colors duration-200 rounded-lg' : ''}
      `}
      onClick={handleClick}
      title={tooltip}
    >
      <div className="text-3xl font-bold text-blue-600 mb-2">
        {completed}/{total}
      </div>
      <div className="text-sm text-gray-600 text-center">
        Background úlohy
      </div>
      {total > 0 && (
        <div className="w-full bg-gray-200 rounded-full h-2 mt-3">
          <div
            className="bg-blue-600 h-2 rounded-full transition-all"
            style={{ width: `${(completed / total) * 100}%` }}
          ></div>
        </div>
      )}
    </div>
  );
};
