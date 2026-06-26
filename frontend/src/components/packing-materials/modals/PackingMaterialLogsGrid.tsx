import React from 'react';
import { format } from 'date-fns';
import { cs } from 'date-fns/locale';
import { PackingMaterialLogDto, LogEntryType } from '../../../api/hooks/usePackingMaterials';

interface PackingMaterialLogsGridProps {
  logs: PackingMaterialLogDto[];
}

const PackingMaterialLogsGrid: React.FC<PackingMaterialLogsGridProps> = ({ logs }) => {
  const formatQuantity = (quantity: number | undefined) => {
    if (quantity === undefined) return '';
    return quantity.toLocaleString('cs-CZ', {
      minimumFractionDigits: 0,
      maximumFractionDigits: 2
    });
  };

  const formatDate = (date: Date | undefined) => {
    return date ? format(date, 'dd.MM.yyyy', { locale: cs }) : '';
  };

  const formatDateTime = (date: Date | undefined) => {
    return date ? format(date, 'dd.MM.yyyy HH:mm', { locale: cs }) : '';
  };

  const getLogTypeColor = (type: LogEntryType | undefined) => {
    switch (type) {
      case LogEntryType.Manual:
        return 'bg-green-100 text-green-800 dark:bg-emerald-900/30 dark:text-emerald-300';
      case LogEntryType.AutomaticConsumption:
        return 'bg-orange-100 text-orange-800 dark:bg-orange-900/30 dark:text-orange-300';
      default:
        return 'bg-gray-100 text-gray-800 dark:bg-graphite-surface-2 dark:text-graphite-muted';
    }
  };

  const getChangeAmountColor = (change: number | undefined) => {
    if (change === undefined) return 'text-gray-600 dark:text-graphite-muted';
    if (change > 0) return 'text-green-600 dark:text-emerald-400';
    if (change < 0) return 'text-red-600 dark:text-red-400';
    return 'text-gray-600 dark:text-graphite-muted';
  };

  if (!logs || logs.length === 0) {
    return (
      <div className="bg-white dark:bg-graphite-surface border border-gray-200 dark:border-graphite-border rounded-lg p-8 text-center">
        <p className="text-gray-500 dark:text-graphite-muted">Žádné záznamy změn za posledních 60 dní</p>
      </div>
    );
  }

  return (
    <div className="bg-white dark:bg-graphite-surface border border-gray-200 dark:border-graphite-border rounded-lg overflow-hidden">
      <div className="overflow-x-auto">
        <table className="min-w-full divide-y divide-gray-200 dark:divide-graphite-border">
          <thead className="bg-gray-50 dark:bg-graphite-surface-2">
            <tr>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">
                Datum
              </th>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">
                Původní množství
              </th>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">
                Nové množství
              </th>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">
                Změna
              </th>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">
                Typ změny
              </th>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">
                Vytvořeno
              </th>
            </tr>
          </thead>
          <tbody className="bg-white dark:bg-graphite-surface divide-y divide-gray-200 dark:divide-graphite-border">
            {logs.map((log) => (
              <tr key={log.id} className="hover:bg-gray-50 dark:hover:bg-white/5">
                <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900 dark:text-graphite-text">
                  {formatDate(log.date)}
                </td>
                <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900 dark:text-graphite-text">
                  {formatQuantity(log.oldQuantity)}
                </td>
                <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900 dark:text-graphite-text">
                  {formatQuantity(log.newQuantity)}
                </td>
                <td className="px-6 py-4 whitespace-nowrap text-sm font-medium">
                  <span className={getChangeAmountColor(log.changeAmount)}>
                    {(log.changeAmount ?? 0) > 0 ? '+' : ''}{formatQuantity(log.changeAmount)}
                  </span>
                </td>
                <td className="px-6 py-4 whitespace-nowrap">
                  <span className={`inline-flex px-2 py-1 text-xs font-semibold rounded-full ${getLogTypeColor(log.logType)}`}>
                    {log.logTypeText}
                  </span>
                </td>
                <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500 dark:text-graphite-muted">
                  {formatDateTime(log.createdAt)}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      
      {/* Summary */}
      <div className="bg-gray-50 dark:bg-graphite-surface-2 px-6 py-3 border-t border-gray-200 dark:border-graphite-border">
        <p className="text-xs text-gray-600 dark:text-graphite-muted">
          Celkem zobrazeno: {logs.length} {logs.length === 1 ? 'záznam' : logs.length < 5 ? 'záznamy' : 'záznamů'}
        </p>
      </div>
    </div>
  );
};

export default PackingMaterialLogsGrid;