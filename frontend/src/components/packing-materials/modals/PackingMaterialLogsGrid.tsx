import React from 'react';
import { format, parseISO } from 'date-fns';
import { cs } from 'date-fns/locale';
import { PackingMaterialLogDto, LogEntryType } from '../../../api/hooks/usePackingMaterials';

interface PackingMaterialLogsGridProps {
  logs: PackingMaterialLogDto[];
}

const PackingMaterialLogsGrid: React.FC<PackingMaterialLogsGridProps> = ({ logs }) => {
  const formatQuantity = (quantity: number) => {
    return quantity.toLocaleString('cs-CZ', {
      minimumFractionDigits: 0,
      maximumFractionDigits: 2
    });
  };

  const formatDate = (dateString: string) => {
    return format(parseISO(dateString), 'dd.MM.yyyy', { locale: cs });
  };

  const formatDateTime = (dateString: string) => {
    return format(parseISO(dateString), 'dd.MM.yyyy HH:mm', { locale: cs });
  };

  const getLogTypeColor = (type: LogEntryType) => {
    switch (type) {
      case LogEntryType.Manual:
        return 'bg-green-100 text-green-800';
      case LogEntryType.AutomaticConsumption:
        return 'bg-orange-100 text-orange-800';
      default:
        return 'bg-gray-100 text-gray-800';
    }
  };

  const getChangeAmountColor = (change: number) => {
    if (change > 0) return 'text-green-600';
    if (change < 0) return 'text-red-600';
    return 'text-gray-600';
  };

  if (!logs || logs.length === 0) {
    return (
      <div className="bg-white border border-gray-200 rounded-lg p-8 text-center">
        <p className="text-gray-500">Žádné záznamy změn za posledních 60 dní</p>
      </div>
    );
  }

  return (
    <div className="bg-white border border-gray-200 rounded-lg overflow-hidden">
      <div className="overflow-x-auto">
        <table className="min-w-full divide-y divide-gray-200">
          <thead className="bg-gray-50">
            <tr>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                Datum
              </th>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                Původní množství
              </th>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                Nové množství
              </th>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                Změna
              </th>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                Typ změny
              </th>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                Vytvořeno
              </th>
            </tr>
          </thead>
          <tbody className="bg-white divide-y divide-gray-200">
            {logs.map((log) => (
              <tr key={log.id} className="hover:bg-gray-50">
                <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                  {formatDate(log.date)}
                </td>
                <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                  {formatQuantity(log.oldQuantity)}
                </td>
                <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                  {formatQuantity(log.newQuantity)}
                </td>
                <td className="px-6 py-4 whitespace-nowrap text-sm font-medium">
                  <span className={getChangeAmountColor(log.changeAmount)}>
                    {log.changeAmount > 0 ? '+' : ''}{formatQuantity(log.changeAmount)}
                  </span>
                </td>
                <td className="px-6 py-4 whitespace-nowrap">
                  <span className={`inline-flex px-2 py-1 text-xs font-semibold rounded-full ${getLogTypeColor(log.logType)}`}>
                    {log.logTypeText}
                  </span>
                </td>
                <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                  {formatDateTime(log.createdAt)}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      
      {/* Summary */}
      <div className="bg-gray-50 px-6 py-3 border-t border-gray-200">
        <p className="text-xs text-gray-600">
          Celkem zobrazeno: {logs.length} {logs.length === 1 ? 'záznam' : logs.length < 5 ? 'záznamy' : 'záznamů'}
        </p>
      </div>
    </div>
  );
};

export default PackingMaterialLogsGrid;