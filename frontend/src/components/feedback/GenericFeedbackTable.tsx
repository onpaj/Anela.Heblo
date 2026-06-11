import React from 'react';
import type { FeedbackRow } from './types';
import { formatDateTime } from '../../utils/formatters';

interface Props {
  rows: FeedbackRow[];
  isLoading: boolean;
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
  onPageChange: (page: number) => void;
  onRowClick: (id: string) => void;
  primaryLabel: string;
}

const ScoreCell: React.FC<{ score: number | null | undefined }> = ({ score }) => {
  if (score == null) return <span className="text-gray-400">–</span>;
  return (
    <span className="inline-flex items-center gap-1">
      {Array.from({ length: 5 }, (_, i) => (
        <span
          key={i}
          className={`inline-block w-2 h-2 rounded-full ${i < score ? 'bg-blue-500' : 'bg-gray-200'}`}
        />
      ))}
    </span>
  );
};

const GenericFeedbackTable: React.FC<Props> = ({
  rows, isLoading, totalCount, pageNumber, pageSize, totalPages, onPageChange, onRowClick, primaryLabel,
}) => {
  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-32 text-sm text-gray-500">Načítám…</div>
    );
  }

  if (rows.length === 0) {
    return (
      <div className="flex items-center justify-center h-32 text-sm text-gray-500">
        Žádné záznamy nenalezeny.
      </div>
    );
  }

  const firstItem = (pageNumber - 1) * pageSize + 1;
  const lastItem = Math.min(pageNumber * pageSize, totalCount);

  return (
    <div className="flex flex-col gap-3">
      <div className="overflow-x-auto rounded-lg border border-gray-200">
        <table className="min-w-full divide-y divide-gray-200 text-sm">
          <thead className="bg-gray-50">
            <tr>
              <th className="px-4 py-3 text-left font-medium text-gray-500 uppercase tracking-wide text-xs whitespace-nowrap">
                Datum
              </th>
              <th className="px-4 py-3 text-left font-medium text-gray-500 uppercase tracking-wide text-xs">
                {primaryLabel}
              </th>
              <th className="px-4 py-3 text-left font-medium text-gray-500 uppercase tracking-wide text-xs whitespace-nowrap">
                Uživatel
              </th>
              <th className="px-4 py-3 text-center font-medium text-gray-500 uppercase tracking-wide text-xs whitespace-nowrap">
                Přesnost
              </th>
              <th className="px-4 py-3 text-center font-medium text-gray-500 uppercase tracking-wide text-xs whitespace-nowrap">
                Styl
              </th>
              <th className="px-4 py-3 text-center font-medium text-gray-500 uppercase tracking-wide text-xs whitespace-nowrap">
                Feedback
              </th>
            </tr>
          </thead>
          <tbody className="bg-white divide-y divide-gray-100">
            {rows.map((row) => (
              <tr
                key={row.id}
                onClick={() => onRowClick(row.id)}
                className="cursor-pointer transition-colors hover:bg-gray-50"
              >
                <td className="px-4 py-3 text-gray-600 whitespace-nowrap">
                  {formatDateTime(row.createdAt)}
                </td>
                <td className="px-4 py-3 text-gray-900 max-w-xs">
                  <span className="line-clamp-2">{row.primaryText}</span>
                </td>
                <td className="px-4 py-3 text-gray-600 whitespace-nowrap">
                  {row.userName ?? row.userId ?? '–'}
                </td>
                <td className="px-4 py-3 text-center">
                  <ScoreCell score={row.precisionScore} />
                </td>
                <td className="px-4 py-3 text-center">
                  <ScoreCell score={row.styleScore} />
                </td>
                <td className="px-4 py-3 text-center">
                  {row.hasFeedback ? (
                    <span className="inline-block px-2 py-0.5 rounded-full text-xs font-medium bg-green-100 text-green-700">
                      Ano
                    </span>
                  ) : (
                    <span className="inline-block px-2 py-0.5 rounded-full text-xs font-medium bg-gray-100 text-gray-500">
                      Ne
                    </span>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <div className="flex items-center justify-between text-sm text-gray-600">
        <span>{firstItem}–{lastItem} z {totalCount}</span>
        <div className="flex items-center gap-1">
          <button onClick={() => onPageChange(1)} disabled={pageNumber === 1}
            className="px-2 py-1 rounded-md disabled:opacity-40 hover:bg-gray-100 disabled:hover:bg-transparent">«</button>
          <button onClick={() => onPageChange(pageNumber - 1)} disabled={pageNumber === 1}
            className="px-2 py-1 rounded-md disabled:opacity-40 hover:bg-gray-100 disabled:hover:bg-transparent">‹</button>
          <span className="px-3 py-1 font-medium text-gray-800">{pageNumber} / {totalPages}</span>
          <button onClick={() => onPageChange(pageNumber + 1)} disabled={pageNumber === totalPages}
            className="px-2 py-1 rounded-md disabled:opacity-40 hover:bg-gray-100 disabled:hover:bg-transparent">›</button>
          <button onClick={() => onPageChange(totalPages)} disabled={pageNumber === totalPages}
            className="px-2 py-1 rounded-md disabled:opacity-40 hover:bg-gray-100 disabled:hover:bg-transparent">»</button>
        </div>
      </div>
    </div>
  );
};

export default GenericFeedbackTable;
