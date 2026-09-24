import React from 'react';
import { ChevronDown, ChevronRight } from 'lucide-react';
import { RecurringJobCategory } from '../../api/hooks/useRecurringJobs';
import { getCategoryLabel } from './recurringJobCategories';

/** Number of columns in the recurring jobs table, used to span the group header row. */
const TABLE_COLUMN_COUNT = 7;

export interface RecurringJobCategorySectionProps {
  category: RecurringJobCategory;
  jobCount: number;
  isExpanded: boolean;
  onToggleExpanded: (category: RecurringJobCategory) => void;
  children: React.ReactNode;
}

const RecurringJobCategorySection: React.FC<RecurringJobCategorySectionProps> = ({
  category,
  jobCount,
  isExpanded,
  onToggleExpanded,
  children
}) => {
  const label = getCategoryLabel(category);

  return (
    <tbody className="divide-y divide-gray-200 dark:divide-graphite-border">
      <tr className="bg-gray-100 dark:bg-graphite-surface-2">
        <th colSpan={TABLE_COLUMN_COUNT} scope="colgroup" className="px-6 py-2 text-left">
          <button
            type="button"
            onClick={() => onToggleExpanded(category)}
            aria-expanded={isExpanded}
            aria-label={`${isExpanded ? 'Sbalit' : 'Rozbalit'} kategorii ${label}`}
            className="inline-flex items-center gap-1.5 text-xs font-semibold uppercase tracking-wider text-gray-600 dark:text-graphite-muted hover:text-gray-900 dark:hover:text-graphite-text"
          >
            {isExpanded ? (
              <ChevronDown className="h-3.5 w-3.5" />
            ) : (
              <ChevronRight className="h-3.5 w-3.5" />
            )}
            {label}
            <span className="font-normal normal-case text-gray-500 dark:text-graphite-faint">
              ({jobCount})
            </span>
          </button>
        </th>
      </tr>
      {isExpanded && children}
    </tbody>
  );
};

export default RecurringJobCategorySection;
