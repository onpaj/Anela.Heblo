import React from 'react';

interface SortColumn {
  value: string;
  label: string;
}

interface Props {
  hasFeedback: boolean | undefined;
  sortBy: string;
  sortDescending: boolean;
  pageSize: number;
  allowedSortColumns: readonly SortColumn[];
  onHasFeedbackChange: (v: boolean | undefined) => void;
  onSortByChange: (v: string) => void;
  onSortDescendingChange: (v: boolean) => void;
  onPageSizeChange: (v: number) => void;
}

const selectClass =
  'border border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text rounded-md text-sm px-2 py-1.5 focus:outline-none focus:ring-2 focus:ring-blue-500';

const GenericFeedbackFilters: React.FC<Props> = ({
  hasFeedback,
  sortBy,
  sortDescending,
  pageSize,
  allowedSortColumns,
  onHasFeedbackChange,
  onSortByChange,
  onSortDescendingChange,
  onPageSizeChange,
}) => (
  <div className="flex flex-wrap gap-3 items-center">
    <div className="flex items-center gap-2">
      <label htmlFor="filter-feedback" className="text-sm text-gray-600 dark:text-graphite-muted whitespace-nowrap">
        Feedback:
      </label>
      <select
        id="filter-feedback"
        value={hasFeedback === undefined ? '' : String(hasFeedback)}
        onChange={(e) =>
          onHasFeedbackChange(
            e.target.value === '' ? undefined : e.target.value === 'true',
          )
        }
        className={selectClass}
      >
        <option value="">Vše</option>
        <option value="true">Pouze s feedbackem</option>
        <option value="false">Pouze bez feedbacku</option>
      </select>
    </div>

    <div className="flex items-center gap-2">
      <label htmlFor="filter-sort" className="text-sm text-gray-600 dark:text-graphite-muted whitespace-nowrap">
        Řadit dle:
      </label>
      <select
        id="filter-sort"
        value={sortBy}
        onChange={(e) => onSortByChange(e.target.value)}
        className={selectClass}
      >
        {allowedSortColumns.map((col) => (
          <option key={col.value} value={col.value}>
            {col.label}
          </option>
        ))}
      </select>
    </div>

    <div className="flex items-center gap-2">
      <label htmlFor="filter-order" className="text-sm text-gray-600 dark:text-graphite-muted whitespace-nowrap">
        Pořadí:
      </label>
      <select
        id="filter-order"
        value={sortDescending ? 'true' : 'false'}
        onChange={(e) => onSortDescendingChange(e.target.value === 'true')}
        className={selectClass}
      >
        <option value="true">Sestupně</option>
        <option value="false">Vzestupně</option>
      </select>
    </div>

    <div className="flex items-center gap-2">
      <label htmlFor="filter-pagesize" className="text-sm text-gray-600 dark:text-graphite-muted whitespace-nowrap">
        Na stránce:
      </label>
      <select
        id="filter-pagesize"
        value={String(pageSize)}
        onChange={(e) => onPageSizeChange(parseInt(e.target.value, 10))}
        className={selectClass}
      >
        <option value="10">10</option>
        <option value="20">20</option>
        <option value="50">50</option>
      </select>
    </div>
  </div>
);

export default GenericFeedbackFilters;
