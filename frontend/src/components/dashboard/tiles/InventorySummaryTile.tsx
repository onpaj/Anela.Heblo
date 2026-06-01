import React from 'react';
import { useNavigate } from 'react-router-dom';
import { 
  createFilteredUrl,
  isTileClickable, 
  getTileTooltip, 
  TileDataWithDrillDown 
} from '../../../utils/urlUtils';

interface InventorySummaryTileProps {
  data: TileDataWithDrillDown & {
    status?: string;
    data?: {
      recent?: number;    // < 120 days
      medium?: number;    // 120-250 days
      old?: number;       // > 250 days
      never?: number;     // never inventoried
      total?: number;
      date?: string;
    };
    error?: string;
  };
  targetUrl?: string; // Base URL for navigation
}

export const InventorySummaryTile: React.FC<InventorySummaryTileProps> = ({ data, targetUrl }) => {
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

  // Extract data with defaults
  const recent = data.data?.recent ?? 0;
  const medium = data.data?.medium ?? 0;
  const old = data.data?.old ?? 0;
  const never = data.data?.never ?? 0;

  // Combine old and never into red category
  const redTotal = old + never;
  const total = recent + medium + redTotal;

  // Calculate percentages for pie chart
  const getPercentage = (value: number) => total > 0 ? (value / total) * 100 : 0;
  const greenPercent = getPercentage(recent);
  const orangePercent = getPercentage(medium);
  const redPercent = getPercentage(redTotal);

  // Create pie chart segments
  const createPieSegment = (startPercent: number, endPercent: number, color: string) => {
    const startAngle = (startPercent / 100) * 2 * Math.PI - Math.PI / 2;
    const endAngle = (endPercent / 100) * 2 * Math.PI - Math.PI / 2;
    const largeArc = endPercent - startPercent > 50 ? 1 : 0;

    const x1 = 50 + 40 * Math.cos(startAngle);
    const y1 = 50 + 40 * Math.sin(startAngle);
    const x2 = 50 + 40 * Math.cos(endAngle);
    const y2 = 50 + 40 * Math.sin(endAngle);

    return `M 50 50 L ${x1} ${y1} A 40 40 0 ${largeArc} 1 ${x2} ${y2} Z`;
  };

  let currentPercent = 0;
  const segments = [];

  if (greenPercent > 0) {
    segments.push({ path: createPieSegment(currentPercent, currentPercent + greenPercent, '#10b981'), color: '#10b981' });
    currentPercent += greenPercent;
  }
  if (orangePercent > 0) {
    segments.push({ path: createPieSegment(currentPercent, currentPercent + orangePercent, '#f59e0b'), color: '#f59e0b' });
    currentPercent += orangePercent;
  }
  if (redPercent > 0) {
    segments.push({ path: createPieSegment(currentPercent, currentPercent + redPercent, '#ef4444'), color: '#ef4444' });
  }

  return (
    <div
      className={`
        flex items-start gap-2 h-full pt-2 min-h-44
        ${isClickable ? 'cursor-pointer hover:bg-gray-50 active:bg-gray-100 transition-colors duration-200 rounded-lg' : ''}
      `}
      onClick={handleClick}
      title={tooltip}
      style={isClickable ? { touchAction: 'manipulation' } : undefined}
    >
      {/* Pie Chart */}
      <div className="flex-shrink-0">
        <svg className="w-48 md:w-32 h-48 md:h-32" viewBox="0 0 100 100">
          {segments.map((segment, index) => (
            <path key={index} d={segment.path} fill={segment.color} />
          ))}
        </svg>
      </div>

      {/* Legend with values */}
      <div className="flex flex-col space-y-2 flex-1 pt-1 leading-relaxed">
        {/* Green: < 120 days */}
        <div className="flex items-center justify-between">
          <div className="flex items-center space-x-2">
            <div className="w-4 h-4 rounded-sm bg-green-500"></div>
            <span className="text-base md:text-sm text-gray-700">&lt; 180 dní</span>
          </div>
          <span className="text-2xl md:text-xl font-bold text-gray-900">{recent}</span>
        </div>

        {/* Orange: 120-250 days */}
        <div className="flex items-center justify-between">
          <div className="flex items-center space-x-2">
            <div className="w-4 h-4 rounded-sm bg-amber-500"></div>
            <span className="text-base md:text-sm text-gray-700">180-365 dní</span>
          </div>
          <span className="text-2xl md:text-xl font-bold text-gray-900">{medium}</span>
        </div>

        {/* Red: > 250 days or never */}
        <div className="flex items-center justify-between">
          <div className="flex items-center space-x-2">
            <div className="w-4 h-4 rounded-sm bg-red-500"></div>
            <span className="text-base md:text-sm text-gray-700">&gt; 365 dní</span>
          </div>
          <span className="text-2xl md:text-xl font-bold text-gray-900">{redTotal}</span>
        </div>
      </div>
    </div>
  );
};
