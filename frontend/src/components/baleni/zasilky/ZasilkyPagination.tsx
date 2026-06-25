interface Props {
  pageNumber: number;
  pageSize: number;
  totalCount: number;
  onPageChange: (page: number) => void;
}

export function ZasilkyPagination({ pageNumber, pageSize, totalCount, onPageChange }: Props) {
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize));
  return (
    <div className="flex items-center justify-between p-4 border-t dark:border-graphite-border bg-white dark:bg-graphite-surface">
      <span className="text-slate-600 dark:text-graphite-muted">
        Celkem {totalCount} · strana {pageNumber} / {totalPages}
      </span>
      <div className="inline-flex gap-2">
        <button
          type="button"
          disabled={pageNumber <= 1}
          onClick={() => onPageChange(pageNumber - 1)}
          className="px-4 py-2 rounded border dark:border-graphite-border dark:text-graphite-muted dark:hover:bg-white/5 disabled:opacity-50"
        >
          Předchozí
        </button>
        <button
          type="button"
          disabled={pageNumber >= totalPages}
          onClick={() => onPageChange(pageNumber + 1)}
          className="px-4 py-2 rounded border dark:border-graphite-border dark:text-graphite-muted dark:hover:bg-white/5 disabled:opacity-50"
        >
          Další
        </button>
      </div>
    </div>
  );
}
