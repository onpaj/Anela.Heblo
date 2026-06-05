import React from 'react';
import { useNavigate } from 'react-router-dom';
import { ShieldCheck, AlertTriangle, XCircle, Clock } from 'lucide-react';
import {
  DashboardTileDrillDown,
  resolveDrillDown,
} from '../drillDownRoutes';

interface DqtYesterdayStatusTileData {
  status?: 'success' | 'warning' | 'error' | 'no_data';
  data?: {
    runId?: string;
    runStatus?: 'Completed' | 'Failed' | 'Running';
    dateFrom?: string;
    dateTo?: string;
    totalChecked?: number;
    totalMismatches?: number;
  } | null;
  drillDown?: DashboardTileDrillDown;
}

interface DqtYesterdayStatusTileProps {
  data: DqtYesterdayStatusTileData;
}

const formatYesterdayLabel = (iso?: string): string => {
  if (!iso) return 'včera';
  const dateOnly = iso.slice(0, 10);
  const parts = dateOnly.split('-');
  if (parts.length !== 3) return 'včera';
  return `${parts[2]}.${parts[1]}.${parts[0]}`;
};

export const DqtYesterdayStatusTile: React.FC<DqtYesterdayStatusTileProps> = ({ data }) => {
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
          <XCircle className="h-10 w-10 text-red-500 mx-auto mb-2" />
          <p className="text-red-600 text-sm">Poslední DQT test selhal</p>
        </div>
      </div>
    );
  }

  if (data.status === 'no_data') {
    return (
      <div
        className="flex flex-col items-center justify-center h-full leading-relaxed min-h-44 cursor-pointer hover:bg-gray-50 active:bg-gray-100 transition-colors duration-200 rounded-lg"
        onClick={handleClick}
        style={{ touchAction: 'manipulation' }}
        data-testid="dqt-yesterday-tile"
      >
        <Clock className="h-10 w-10 text-gray-400 mb-2" />
        <p className="text-sm text-gray-500">Žádná data</p>
        <p className="text-xs text-gray-400 mt-1">Včerejší test neproběhl</p>
      </div>
    );
  }

  const runStatus = data.data?.runStatus;
  const dateLabel = formatYesterdayLabel(data.data?.dateTo);

  if (data.status === 'warning' && runStatus === 'Running') {
    return (
      <div
        className="flex flex-col items-center justify-center h-full leading-relaxed min-h-44 cursor-pointer hover:bg-gray-50 active:bg-gray-100 transition-colors duration-200 rounded-lg"
        onClick={handleClick}
        style={{ touchAction: 'manipulation' }}
        data-testid="dqt-yesterday-tile"
      >
        <Clock className="h-10 w-10 text-amber-500 mb-2" />
        <p className="text-sm text-amber-600">probíhá</p>
        <p className="text-xs text-gray-400 mt-1">{dateLabel}</p>
      </div>
    );
  }

  const totalMismatches = data.data?.totalMismatches ?? 0;
  const totalChecked = data.data?.totalChecked ?? 0;
  const hasMismatches = totalMismatches > 0;
  const iconColor = hasMismatches ? 'text-red-500' : 'text-green-500';
  const countColor = hasMismatches ? 'text-red-700' : 'text-green-700';

  return (
    <div
      className="flex flex-col items-center justify-center h-full leading-relaxed min-h-44 cursor-pointer hover:bg-gray-50 active:bg-gray-100 transition-colors duration-200 rounded-lg"
      onClick={handleClick}
      style={{ touchAction: 'manipulation' }}
      data-testid="dqt-yesterday-tile"
    >
      <div className={`mb-2 ${iconColor}`}>
        {hasMismatches ? (
          <AlertTriangle className="h-10 w-10" />
        ) : (
          <ShieldCheck className="h-10 w-10" />
        )}
      </div>
      <div className={`text-3xl font-bold mb-1 ${countColor}`}>{totalMismatches}</div>
      <div className="text-sm text-gray-500">{hasMismatches ? 'neshod' : 'vše OK'}</div>
      {totalChecked > 0 && (
        <div className="text-xs text-gray-400 mt-1">z {totalChecked} faktur</div>
      )}
      <div className="text-xs text-gray-400 mt-1">{dateLabel}</div>
    </div>
  );
};
