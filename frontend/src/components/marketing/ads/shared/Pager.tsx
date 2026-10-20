import React from 'react';

interface PagerProps {
  page: number;
  pageSize: number;
  totalCount: number;
  onPageChange: (page: number) => void;
}

const PAGER_BUTTON =
  'px-2 py-1 border border-gray-300 dark:border-graphite-border rounded text-gray-700 dark:text-graphite-muted bg-white dark:bg-graphite-surface hover:bg-gray-50 dark:hover:bg-white/5 disabled:opacity-50 disabled:cursor-not-allowed';

const Pager: React.FC<PagerProps> = ({ page, pageSize, totalCount, onPageChange }) => {
  if (totalCount <= pageSize) {
    return null;
  }
  const totalPages = Math.ceil(totalCount / pageSize);

  return (
    <div className="flex-shrink-0 flex items-center justify-between px-3 py-2 text-xs text-gray-600 dark:text-graphite-muted border-t border-gray-200 dark:border-graphite-border">
      <span>{`Strana ${page} z ${totalPages} · celkem ${totalCount}`}</span>
      <div className="flex gap-2">
        <button type="button" className={PAGER_BUTTON} onClick={() => onPageChange(page - 1)} disabled={page <= 1}>
          Předchozí
        </button>
        <button type="button" className={PAGER_BUTTON} onClick={() => onPageChange(page + 1)} disabled={page >= totalPages}>
          Další
        </button>
      </div>
    </div>
  );
};

export default Pager;
