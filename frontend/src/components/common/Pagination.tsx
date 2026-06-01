import React from 'react';
import { ChevronLeft, ChevronRight } from 'lucide-react';

interface PaginationProps {
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
  onPageChange: (page: number) => void;
  onPageSizeChange: (size: number) => void;
  isFiltered?: boolean;
}

const Pagination: React.FC<PaginationProps> = ({
  totalCount,
  pageNumber,
  pageSize,
  totalPages,
  onPageChange,
  onPageSizeChange,
  isFiltered = false,
}) => {
  if (totalCount === 0) return null;

  return (
    <div className="flex-shrink-0 bg-white px-3 py-2 flex items-center justify-between border-t border-gray-200 text-xs">
      {/* Mobile: prev/next only */}
      <div className="flex-1 flex justify-between sm:hidden">
        <button
          onClick={() => onPageChange(pageNumber - 1)}
          disabled={pageNumber <= 1}
          className="relative inline-flex items-center px-2 py-1 border border-gray-300 text-xs font-medium rounded text-gray-700 bg-white hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
        >
          Předchozí
        </button>
        <button
          onClick={() => onPageChange(pageNumber + 1)}
          disabled={pageNumber >= totalPages}
          className="ml-2 relative inline-flex items-center px-2 py-1 border border-gray-300 text-xs font-medium rounded text-gray-700 bg-white hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
        >
          Další
        </button>
      </div>
      {/* Desktop */}
      <div className="hidden sm:flex-1 sm:flex sm:items-center sm:justify-between">
        <div className="flex items-center space-x-3">
          <p className="text-xs text-gray-600">
            {Math.min((pageNumber - 1) * pageSize + 1, totalCount)}-
            {Math.min(pageNumber * pageSize, totalCount)} z {totalCount}
            {isFiltered ? <span className="text-gray-500"> (filtrováno)</span> : ''}
          </p>
          <div className="flex items-center space-x-1">
            <span className="text-xs text-gray-600">Zobrazit:</span>
            <select
              id="pageSize"
              value={pageSize}
              onChange={(e) => onPageSizeChange(Number(e.target.value))}
              className="border border-gray-300 rounded px-1 py-0.5 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-transparent"
            >
              <option value={10}>10</option>
              <option value={20}>20</option>
              <option value={50}>50</option>
              <option value={100}>100</option>
            </select>
          </div>
        </div>
        <div>
          <nav
            className="relative z-0 inline-flex rounded shadow-sm -space-x-px"
            aria-label="Pagination"
          >
            <button
              onClick={() => onPageChange(pageNumber - 1)}
              disabled={pageNumber <= 1}
              className="relative inline-flex items-center px-1 py-1 rounded-l border border-gray-300 bg-white text-xs font-medium text-gray-500 hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
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
                  onClick={() => onPageChange(pageNum)}
                  className={`relative inline-flex items-center px-2 py-1 border text-xs font-medium ${
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
              onClick={() => onPageChange(pageNumber + 1)}
              disabled={pageNumber >= totalPages}
              className="relative inline-flex items-center px-1 py-1 rounded-r border border-gray-300 bg-white text-xs font-medium text-gray-500 hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
            >
              <ChevronRight className="h-3 w-3" />
            </button>
          </nav>
        </div>
      </div>
    </div>
  );
};

export default Pagination;
