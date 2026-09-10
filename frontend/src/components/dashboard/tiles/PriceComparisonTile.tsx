import React from 'react';
import { useNavigate } from 'react-router-dom';
import { ShieldCheck, AlertTriangle, XCircle, Clock } from 'lucide-react';
import {
  DashboardTileDrillDown,
  resolveDrillDown,
} from '../drillDownRoutes';

interface PriceComparisonTileProps {
  data: {
    status?: string;
    data?: {
      totalChecked?: number;
      totalMismatches?: number;
      completedAt?: string;
    };
    error?: string;
    drillDown?: DashboardTileDrillDown;
  };
}

export const PriceComparisonTile: React.FC<PriceComparisonTileProps> = ({ data }) => {
  const navigate = useNavigate();
  const resolution = resolveDrillDown(data.drillDown);

  const handleClick = () => {
    if (!resolution) {
      return;
    }
    if (resolution.strategy === 'react-router') {
      navigate(resolution.url);
    } else {
      window.open(resolution.url, '_blank');
    }
  };

  if (data.status === 'error') {
    return (
      <div className="h-full flex items-center justify-center text-center">
        <div>
          <XCircle className="h-10 w-10 text-red-500 dark:text-red-400 mx-auto mb-2" />
          <p className="text-red-600 dark:text-red-400 text-sm">{data.error || 'Poslední kontrola cen selhala'}</p>
        </div>
      </div>
    );
  }

  if (data.status === 'no_data') {
    return (
      <div
        className="flex flex-col items-center justify-center h-full leading-relaxed min-h-44 cursor-pointer hover:bg-gray-50 dark:hover:bg-white/5 active:bg-gray-100 dark:active:bg-white/10 transition-colors duration-200 rounded-lg"
        onClick={handleClick}
        style={{ touchAction: 'manipulation' }}
      >
        <Clock className="h-10 w-10 text-gray-400 dark:text-graphite-faint mb-2" />
        <p className="text-sm text-gray-500 dark:text-graphite-muted">Žádná data</p>
        <p className="text-xs text-gray-400 dark:text-graphite-faint mt-1">Spusťte první kontrolu</p>
      </div>
    );
  }

  const totalMismatches = data.data?.totalMismatches ?? 0;
  const totalChecked = data.data?.totalChecked ?? 0;

  const hasMismatches = totalMismatches > 0;
  const iconColor = hasMismatches ? 'text-red-500 dark:text-red-400' : 'text-green-500 dark:text-emerald-400';
  const countColor = hasMismatches ? 'text-red-700 dark:text-red-400' : 'text-green-700 dark:text-emerald-400';

  return (
    <div
      className="flex flex-col items-center justify-center h-full leading-relaxed min-h-44 cursor-pointer hover:bg-gray-50 dark:hover:bg-white/5 active:bg-gray-100 dark:active:bg-white/10 transition-colors duration-200 rounded-lg"
      onClick={handleClick}
      style={{ touchAction: 'manipulation' }}
    >
      <div className={`mb-2 ${iconColor}`}>
        {hasMismatches ? (
          <AlertTriangle className="h-10 w-10" />
        ) : (
          <ShieldCheck className="h-10 w-10" />
        )}
      </div>
      <div className={`text-3xl font-bold mb-1 ${countColor}`}>
        {totalMismatches}
      </div>
      <div className="text-sm text-gray-500 dark:text-graphite-muted">
        {hasMismatches ? 'neshod' : 'vše OK'}
      </div>
      {totalChecked > 0 && (
        <div className="text-xs text-gray-400 dark:text-graphite-faint mt-1">
          ze {totalChecked} produktů
        </div>
      )}
    </div>
  );
};
