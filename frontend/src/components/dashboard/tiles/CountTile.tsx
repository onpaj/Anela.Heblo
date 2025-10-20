import React from 'react';
import { useNavigate } from 'react-router-dom';
import { 
  createFilteredUrl,
  isTileClickable, 
  getTileTooltip, 
  TileDataWithDrillDown 
} from '../../../utils/urlUtils';

interface CountTileProps {
  data: TileDataWithDrillDown;
  icon: React.ReactNode;
  iconColor?: string;
  tileCategory?: string;
  tileTitle?: string;
  targetUrl?: string; // Base URL that this tile should navigate to
}

export const CountTile: React.FC<CountTileProps> = ({ data, icon, iconColor = 'text-indigo-600', tileCategory, tileTitle, targetUrl }) => {
  const navigate = useNavigate();
  
  const isClickable = isTileClickable(data);
  const tooltip = getTileTooltip(data);

  const handleClick = () => {
    if (isClickable && targetUrl && data.drillDown?.filters) {
      const url = createFilteredUrl(targetUrl, data.drillDown.filters);
      navigate(url);
    }
  };

  // Error state
  if (data.status === 'error') {
    return (
      <div className="h-full flex items-center justify-center text-center">
        <div>
          <div className="text-red-500 text-2xl mb-2">⚠️</div>
          <p className="text-red-600 text-sm">{data.error || 'Chyba při načítání dat'}</p>
        </div>
      </div>
    );
  }

  // Extract count from data
  const count = data.data?.count ?? 0;

  return (
    <div 
      className={`
        flex flex-col items-center justify-center h-full
        ${isClickable ? 'cursor-pointer hover:bg-gray-50 transition-colors duration-200 rounded-lg' : ''}
      `}
      onClick={handleClick}
      title={tooltip}
    >
      <div className={`mb-2 ${iconColor}`}>
        {icon}
      </div>
      <div className="text-3xl font-bold text-gray-900">
        {count}
      </div>
      {isClickable && (
        <div className="text-xs text-gray-500 mt-1 opacity-0 group-hover:opacity-100 transition-opacity">
          Klikněte pro detail
        </div>
      )}
    </div>
  );
};
