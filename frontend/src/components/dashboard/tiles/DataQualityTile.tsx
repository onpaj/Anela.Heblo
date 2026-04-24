import React from 'react';
import { useNavigate } from 'react-router-dom';
import { ShieldCheck, AlertTriangle, XCircle, Clock } from 'lucide-react';

interface DataQualityTileProps {
  data: {
    status?: string;
    data?: {
      mismatchCount?: number;
      totalChecked?: number;
      dateFrom?: string;
      dateTo?: string;
    };
    error?: string;
  };
}

export const DataQualityTile: React.FC<DataQualityTileProps> = ({ data }) => {
  const navigate = useNavigate();

  const handleClick = () => {
    navigate('/data-quality');
  };

  if (data.status === 'error') {
    return (
      <div className="h-full flex items-center justify-center text-center">
        <div>
          <XCircle className="h-10 w-10 text-red-500 mx-auto mb-2" />
          <p className="text-red-600 text-sm">{data.error || 'Chyba při načítání dat'}</p>
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
      >
        <Clock className="h-10 w-10 text-gray-400 mb-2" />
        <p className="text-sm text-gray-500">Žádná data</p>
        <p className="text-xs text-gray-400 mt-1">Spusťte první kontrolu</p>
      </div>
    );
  }

  const mismatchCount = data.data?.mismatchCount ?? 0;
  const totalChecked = data.data?.totalChecked ?? 0;
  const dateFrom = data.data?.dateFrom;
  const dateTo = data.data?.dateTo;

  const hasMismatches = mismatchCount > 0;
  const iconColor = hasMismatches ? 'text-red-500' : 'text-green-500';
  const countColor = hasMismatches ? 'text-red-700' : 'text-green-700';

  const dateRange =
    dateFrom && dateTo
      ? `${dateFrom} – ${dateTo}`
      : null;

  return (
    <div
      className="flex flex-col items-center justify-center h-full leading-relaxed min-h-44 cursor-pointer hover:bg-gray-50 active:bg-gray-100 transition-colors duration-200 rounded-lg"
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
        {mismatchCount}
      </div>
      <div className="text-sm text-gray-500">
        {hasMismatches ? 'neshod' : 'vše OK'}
      </div>
      {totalChecked > 0 && (
        <div className="text-xs text-gray-400 mt-1">
          z {totalChecked} faktur
        </div>
      )}
      {dateRange && (
        <div className="text-xs text-gray-400 mt-1">
          {dateRange}
        </div>
      )}
    </div>
  );
};
