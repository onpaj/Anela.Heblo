import React from 'react';
import { useNavigate } from 'react-router-dom';
import {
  createFilteredUrl,
  isTileClickable,
  getTileTooltip,
  TileDataWithDrillDown
} from '../../../utils/urlUtils';

interface MaterialExpirationSummaryTileProps {
  data: TileDataWithDrillDown & {
    status?: string;
    data?: {
      expired?: number;    // already past expiration
      within30?: number;   // expiring within 30 days
      within90?: number;   // expiring within 31-90 days
      ok?: number;         // healthy: expiring beyond the 90-day horizon
      total?: number;
      date?: string;
    };
    error?: string;
  };
  targetUrl?: string; // Base URL for navigation
}

export const MaterialExpirationSummaryTile: React.FC<MaterialExpirationSummaryTileProps> = ({ data, targetUrl }) => {
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
          <div className="text-red-500 dark:text-red-400 text-2xl mb-2">⚠️</div>
          <p className="text-red-600 dark:text-red-400 text-sm">{data.error || 'Chyba při načítání dat'}</p>
        </div>
      </div>
    );
  }

  // Extract data with defaults (urgency order: expired first)
  const expired = data.data?.expired ?? 0;
  const within30 = data.data?.within30 ?? 0;
  const within90 = data.data?.within90 ?? 0;
  const ok = data.data?.ok ?? 0;

  // Include healthy materials so the pie shows a real proportion, not just problem buckets
  const total = expired + within30 + within90 + ok;

  // Calculate percentages for pie chart
  const getPercentage = (value: number) => total > 0 ? (value / total) * 100 : 0;
  const expiredPercent = getPercentage(expired);
  const within30Percent = getPercentage(within30);
  const within90Percent = getPercentage(within90);
  const okPercent = getPercentage(ok);

  // Create pie chart segments
  const createPieSegment = (startPercent: number, endPercent: number) => {
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

  if (expiredPercent > 0) {
    segments.push({ path: createPieSegment(currentPercent, currentPercent + expiredPercent), color: '#ef4444' });
    currentPercent += expiredPercent;
  }
  if (within30Percent > 0) {
    segments.push({ path: createPieSegment(currentPercent, currentPercent + within30Percent), color: '#f97316' });
    currentPercent += within30Percent;
  }
  if (within90Percent > 0) {
    segments.push({ path: createPieSegment(currentPercent, currentPercent + within90Percent), color: '#f59e0b' });
    currentPercent += within90Percent;
  }
  if (okPercent > 0) {
    segments.push({ path: createPieSegment(currentPercent, currentPercent + okPercent), color: '#22c55e' });
  }

  return (
    <div
      className={`
        flex items-start gap-2 h-full pt-2 min-h-44
        ${isClickable ? 'cursor-pointer hover:bg-gray-50 dark:hover:bg-white/5 active:bg-gray-100 dark:active:bg-white/10 transition-colors duration-200 rounded-lg' : ''}
      `}
      onClick={handleClick}
      title={tooltip}
      style={isClickable ? { touchAction: 'manipulation' } : undefined}
    >
      {/* Pie Chart */}
      <div className="flex-shrink-0">
        <svg className="w-48 md:w-32 h-48 md:h-32" viewBox="0 0 100 100">
          {segments.length === 1 ? (
            <circle cx="50" cy="50" r="40" fill={segments[0].color} />
          ) : (
            segments.map((segment, index) => (
              <path key={index} d={segment.path} fill={segment.color} />
            ))
          )}
        </svg>
      </div>

      {/* Legend with values */}
      <div className="flex flex-col space-y-2 flex-1 pt-1 leading-relaxed">
        {/* Red: expired */}
        <div className="flex items-center justify-between">
          <div className="flex items-center space-x-2">
            <div className="w-4 h-4 rounded-sm bg-red-500"></div>
            <span className="text-base md:text-sm text-gray-700 dark:text-graphite-muted">Po expiraci</span>
          </div>
          <span className="text-2xl md:text-xl font-bold text-gray-900 dark:text-graphite-text">{expired}</span>
        </div>

        {/* Orange: within 30 days */}
        <div className="flex items-center justify-between">
          <div className="flex items-center space-x-2">
            <div className="w-4 h-4 rounded-sm bg-orange-500"></div>
            <span className="text-base md:text-sm text-gray-700 dark:text-graphite-muted">&le; 30 dní</span>
          </div>
          <span className="text-2xl md:text-xl font-bold text-gray-900 dark:text-graphite-text">{within30}</span>
        </div>

        {/* Amber: within 90 days */}
        <div className="flex items-center justify-between">
          <div className="flex items-center space-x-2">
            <div className="w-4 h-4 rounded-sm bg-amber-500"></div>
            <span className="text-base md:text-sm text-gray-700 dark:text-graphite-muted">31–90 dní</span>
          </div>
          <span className="text-2xl md:text-xl font-bold text-gray-900 dark:text-graphite-text">{within90}</span>
        </div>

        {/* Green: healthy (more than 90 days) */}
        <div className="flex items-center justify-between">
          <div className="flex items-center space-x-2">
            <div className="w-4 h-4 rounded-sm bg-green-500"></div>
            <span className="text-base md:text-sm text-gray-700 dark:text-graphite-muted">V pořádku</span>
          </div>
          <span className="text-2xl md:text-xl font-bold text-gray-900 dark:text-graphite-text">{ok}</span>
        </div>
      </div>
    </div>
  );
};
