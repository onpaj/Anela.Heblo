import React from 'react';
import { CheckCircle, AlertTriangle, XCircle, HelpCircle, Loader2 } from 'lucide-react';
import { DqtRunDto } from '../../api/hooks/useDataQuality';

interface DqtSummaryCardsProps {
  run: DqtRunDto | null | undefined;
  isLoading: boolean;
}

const DqtSummaryCards: React.FC<DqtSummaryCardsProps> = ({ run, isLoading }) => {
  const getStatusInfo = () => {
    if (!run) {
      return {
        label: 'Žádná data',
        icon: <HelpCircle className="h-6 w-6 text-gray-400" />,
        cardClass: 'border-gray-200',
        labelClass: 'text-gray-500',
      };
    }
    if (run.status === 'Failed') {
      return {
        label: 'Chyba',
        icon: <XCircle className="h-6 w-6 text-red-500" />,
        cardClass: 'border-red-200',
        labelClass: 'text-red-600',
      };
    }
    if (run.status === 'Completed' && run.totalMismatches > 0) {
      return {
        label: 'Neshody',
        icon: <AlertTriangle className="h-6 w-6 text-yellow-500" />,
        cardClass: 'border-yellow-200',
        labelClass: 'text-yellow-600',
      };
    }
    if (run.status === 'Completed' && run.totalMismatches === 0) {
      return {
        label: 'OK',
        icon: <CheckCircle className="h-6 w-6 text-green-500" />,
        cardClass: 'border-green-200',
        labelClass: 'text-green-600',
      };
    }
    // Running or unknown
    return {
      label: run.status,
      icon: <Loader2 className="h-6 w-6 text-indigo-500 animate-spin" />,
      cardClass: 'border-indigo-200',
      labelClass: 'text-indigo-600',
    };
  };

  const statusInfo = getStatusInfo();

  if (isLoading) {
    return (
      <div className="grid grid-cols-1 md:grid-cols-4 gap-4">
        {[1, 2, 3, 4].map((i) => (
          <div key={i} className="bg-white rounded-lg border border-gray-200 p-4 animate-pulse">
            <div className="h-4 bg-gray-200 rounded w-1/2 mb-2" />
            <div className="h-8 bg-gray-200 rounded w-3/4" />
          </div>
        ))}
      </div>
    );
  }

  return (
    <div className="grid grid-cols-1 md:grid-cols-4 gap-4">
      {/* Status card */}
      <div className={`bg-white rounded-lg border p-4 ${statusInfo.cardClass}`}>
        <h3 className="text-sm font-medium text-gray-600 mb-2">Stav</h3>
        <div className="flex items-center gap-2">
          {statusInfo.icon}
          <span className={`text-lg font-bold ${statusInfo.labelClass}`}>
            {statusInfo.label}
          </span>
        </div>
        {run?.status === 'Running' && (
          <p className="text-xs text-gray-400 mt-1">Probíhá...</p>
        )}
      </div>

      {/* Mismatches card */}
      <div className="bg-white rounded-lg border border-gray-200 p-4">
        <h3 className="text-sm font-medium text-gray-600 mb-2">Neshody</h3>
        <p
          className={`text-2xl font-bold ${
            run == null
              ? 'text-gray-400'
              : run.totalMismatches > 0
                ? 'text-red-600'
                : 'text-green-600'
          }`}
        >
          {run != null ? run.totalMismatches : '—'}
        </p>
      </div>

      {/* Checked card */}
      <div className="bg-white rounded-lg border border-gray-200 p-4">
        <h3 className="text-sm font-medium text-gray-600 mb-2">Zkontrolováno</h3>
        <p className="text-2xl font-bold text-gray-900">
          {run != null ? run.totalChecked : '—'}
        </p>
      </div>

      {/* Period card */}
      <div className="bg-white rounded-lg border border-gray-200 p-4">
        <h3 className="text-sm font-medium text-gray-600 mb-2">Období</h3>
        <p className="text-sm font-medium text-gray-900">
          {run != null ? (
            <>
              {run.dateFrom} — {run.dateTo}
            </>
          ) : (
            '—'
          )}
        </p>
      </div>
    </div>
  );
};

export default DqtSummaryCards;
