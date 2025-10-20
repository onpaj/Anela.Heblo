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
    completed?: number;
    total?: number;
    status?: string;
  };
}

export const BackgroundTasksTile: React.FC<BackgroundTasksTileProps> = ({ data }) => {
  const navigate = useNavigate();
  const { completed = 0, total = 0, status = '0/0' } = data;
  
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
        {status}
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
