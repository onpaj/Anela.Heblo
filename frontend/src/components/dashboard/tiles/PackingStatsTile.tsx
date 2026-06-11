import React from 'react';
import { useNavigate } from 'react-router-dom';

interface PackerStat {
  packerId: string | null;
  packerName: string;
  orderCount: number;
}

interface PackingStatsData {
  ordersBeingPackedCount: number | null;
  totalOrdersPackedToday: number;
  packedByPacker: PackerStat[];
}

interface PackingStatsTileProps {
  data: {
    status: string;
    error?: string;
    data?: PackingStatsData;
    drillDown?: {
      enabled: boolean;
      tooltip?: string;
    };
  };
}

export const PackingStatsTile: React.FC<PackingStatsTileProps> = ({ data }) => {
  const navigate = useNavigate();

  if (data.status === 'error') {
    return (
      <div className="h-full flex items-center justify-center text-center">
        <p className="text-red-600 text-sm">{data.error || 'Chyba při načítání dat'}</p>
      </div>
    );
  }

  const stats = data.data;
  if (!stats) return null;

  const isClickable = data.drillDown?.enabled ?? false;

  return (
    <div
      className={`h-full flex flex-col gap-3 ${isClickable ? 'cursor-pointer' : ''}`}
      onClick={isClickable ? () => navigate('/baleni') : undefined}
      title={data.drillDown?.tooltip}
    >
      <div className="grid grid-cols-2 gap-3">
        <div className="bg-secondary-blue-pale rounded-lg p-3 text-center">
          <p className="text-xs text-neutral-gray mb-1">Balí se</p>
          <p className="text-2xl font-bold text-primary-blue">
            {stats.ordersBeingPackedCount ?? '—'}
          </p>
        </div>
        <div className="bg-secondary-blue-pale rounded-lg p-3 text-center">
          <p className="text-xs text-neutral-gray mb-1">Zabaleno dnes</p>
          <p className="text-2xl font-bold text-primary-blue">
            {stats.totalOrdersPackedToday}
          </p>
        </div>
      </div>

      {stats.packedByPacker.length > 0 && (
        <ul className="space-y-1 flex-1 overflow-y-auto">
          {stats.packedByPacker.map((p) => (
            <li
              key={p.packerId ?? p.packerName}
              className="flex items-center justify-between py-1.5 px-2 bg-secondary-blue-pale rounded"
            >
              <span className="text-xs font-medium text-neutral-slate truncate">{p.packerName}</span>
              <span className="text-xs font-bold text-primary-blue ml-2 flex-shrink-0">{p.orderCount}</span>
            </li>
          ))}
        </ul>
      )}

      {stats.packedByPacker.length === 0 && (
        <p className="text-xs text-neutral-gray italic">Dnes zatím nikdo nezabalil žádnou objednávku.</p>
      )}
    </div>
  );
};
