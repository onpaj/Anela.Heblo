import React, { useState } from 'react';
import {
  CheckCircle,
  AlertTriangle,
  XCircle,
  Loader2,
  ChevronLeft,
  ChevronRight,
  AlertCircle,
} from 'lucide-react';
import { useDqtRuns, DqtRunDto } from '../../api/hooks/useDataQuality';

interface DqtRunsTableProps {
  onRunSelect: (runId: string) => void;
  selectedRunId: string | null;
}

const PAGE_SIZE = 20;

const formatDateTime = (iso: string): string => {
  if (!iso) return '—';
  const d = new Date(iso);
  return d.toLocaleString('cs-CZ', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
};

const StatusIcon: React.FC<{ run: DqtRunDto }> = ({ run }) => {
  if (run.status === 'Failed') {
    return <XCircle className="h-4 w-4 text-red-500" />;
  }
  if (run.status === 'Running') {
    return <Loader2 className="h-4 w-4 text-indigo-500 animate-spin" />;
  }
  if (run.status === 'Completed' && run.totalMismatches > 0) {
    return <AlertTriangle className="h-4 w-4 text-yellow-500" />;
  }
  return <CheckCircle className="h-4 w-4 text-green-500" />;
};

const DqtRunsTable: React.FC<DqtRunsTableProps> = ({ onRunSelect, selectedRunId }) => {
  const [pageNumber, setPageNumber] = useState(1);

  const { data, isLoading, error } = useDqtRuns({ pageNumber, pageSize: PAGE_SIZE });

  const items = data?.items ?? [];
  const totalPages = data?.totalPages ?? 1;
  const totalCount = data?.totalCount ?? 0;

  const handlePageChange = (newPage: number) => {
    if (newPage >= 1 && newPage <= totalPages) {
      setPageNumber(newPage);
    }
  };

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-40">
        <div className="flex items-center gap-2 text-gray-500">
          <Loader2 className="h-5 w-5 animate-spin text-indigo-500" />
          Načítání testů...
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="flex items-center justify-center h-40">
        <div className="flex items-center gap-2 text-red-600">
          <AlertCircle className="h-5 w-5" />
          Chyba při načítání: {(error as Error).message}
        </div>
      </div>
    );
  }

  return (
    <div className="flex flex-col h-full">
      <div className="flex-1 overflow-auto">
        <table className="min-w-full divide-y divide-gray-200">
          <thead className="bg-gray-50 sticky top-0 z-10">
            <tr>
              <th className="px-3 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider w-8" />
              <th className="px-3 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                Období
              </th>
              <th className="px-3 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                Zkontrolováno
              </th>
              <th className="px-3 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                Neshody
              </th>
              <th className="px-3 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                Spuštění
              </th>
              <th className="px-3 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                Čas
              </th>
            </tr>
          </thead>
          <tbody className="bg-white divide-y divide-gray-200">
            {items.map((run) => {
              const isSelected = run.id === selectedRunId;
              return (
                <tr
                  key={run.id}
                  onClick={() => onRunSelect(run.id)}
                  className={`cursor-pointer transition-colors ${
                    isSelected
                      ? 'bg-indigo-50 border-l-2 border-indigo-500'
                      : 'hover:bg-gray-50'
                  }`}
                >
                  <td className="px-3 py-3">
                    <StatusIcon run={run} />
                  </td>
                  <td className="px-3 py-3 text-sm text-gray-900 whitespace-nowrap">
                    {run.dateFrom} — {run.dateTo}
                  </td>
                  <td className="px-3 py-3 text-sm text-gray-700 text-center">
                    {run.totalChecked}
                  </td>
                  <td className="px-3 py-3 text-sm font-medium text-center">
                    <span
                      className={run.totalMismatches > 0 ? 'text-red-600' : 'text-green-600'}
                    >
                      {run.totalMismatches}
                    </span>
                  </td>
                  <td className="px-3 py-3">
                    <span
                      className={`inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium ${
                        run.triggerType === 'Manual'
                          ? 'bg-indigo-100 text-indigo-800'
                          : 'bg-gray-100 text-gray-700'
                      }`}
                    >
                      {run.triggerType === 'Manual' ? 'Ruční' : 'Plánované'}
                    </span>
                  </td>
                  <td className="px-3 py-3 text-sm text-gray-500 whitespace-nowrap">
                    {formatDateTime(run.startedAt)}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>

        {items.length === 0 && (
          <div className="text-center py-8 text-gray-500 text-sm">Žádné testy nebyly nalezeny.</div>
        )}
      </div>

      {/* Pagination */}
      {totalCount > 0 && (
        <div className="flex-shrink-0 bg-white border-t border-gray-200 px-3 py-2 flex items-center justify-between text-xs">
          <p className="text-gray-600">
            {Math.min((pageNumber - 1) * PAGE_SIZE + 1, totalCount)}–
            {Math.min(pageNumber * PAGE_SIZE, totalCount)} z {totalCount}
          </p>
          <nav className="inline-flex rounded shadow-sm -space-x-px">
            <button
              onClick={() => handlePageChange(pageNumber - 1)}
              disabled={pageNumber <= 1}
              className="inline-flex items-center px-2 py-1 rounded-l border border-gray-300 bg-white text-xs font-medium text-gray-500 hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
            >
              <ChevronLeft className="h-3 w-3" />
            </button>
            {Array.from({ length: Math.min(totalPages, 5) }, (_, i) => {
              let pageNum: number;
              if (totalPages <= 5) {
                pageNum = i + 1;
              } else if (pageNumber <= 3) {
                pageNum = i + 1;
              } else if (pageNumber >= totalPages - 2) {
                pageNum = totalPages - 4 + i;
              } else {
                pageNum = pageNumber - 2 + i;
              }
              return (
                <button
                  key={pageNum}
                  onClick={() => handlePageChange(pageNum)}
                  className={`inline-flex items-center px-2 py-1 border text-xs font-medium ${
                    pageNum === pageNumber
                      ? 'z-10 bg-indigo-50 border-indigo-500 text-indigo-600'
                      : 'bg-white border-gray-300 text-gray-500 hover:bg-gray-50'
                  }`}
                >
                  {pageNum}
                </button>
              );
            })}
            <button
              onClick={() => handlePageChange(pageNumber + 1)}
              disabled={pageNumber >= totalPages}
              className="inline-flex items-center px-2 py-1 rounded-r border border-gray-300 bg-white text-xs font-medium text-gray-500 hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
            >
              <ChevronRight className="h-3 w-3" />
            </button>
          </nav>
        </div>
      )}
    </div>
  );
};

export default DqtRunsTable;
