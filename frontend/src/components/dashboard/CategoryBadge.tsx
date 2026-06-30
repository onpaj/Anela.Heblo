import React from 'react';
import { useTranslation } from 'react-i18next';

interface CategoryBadgeProps {
  category: string;
}

const categoryColors: Record<string, string> = {
  Manufacture: 'bg-blue-100 text-blue-700 border-blue-200 dark:bg-blue-900/30 dark:text-blue-300 dark:border-blue-900/40',
  System: 'bg-gray-100 text-gray-700 border-gray-200 dark:bg-graphite-surface-2 dark:text-graphite-muted dark:border-graphite-border',
  Warehouse: 'bg-green-100 text-green-700 border-green-200 dark:bg-emerald-900/30 dark:text-emerald-300 dark:border-emerald-900/40',
  Purchase: 'bg-purple-100 text-purple-700 border-purple-200 dark:bg-purple-900/30 dark:text-purple-300 dark:border-purple-900/40',
  Finance: 'bg-yellow-100 text-yellow-700 border-yellow-200 dark:bg-amber-900/30 dark:text-amber-300 dark:border-amber-900/40',
  Orders: 'bg-indigo-100 text-indigo-700 border-indigo-200 dark:bg-indigo-900/30 dark:text-indigo-300 dark:border-indigo-900/40',
  Error: 'bg-red-100 text-red-700 border-red-200 dark:bg-red-900/30 dark:text-red-300 dark:border-red-900/40',
};

const CategoryBadge: React.FC<CategoryBadgeProps> = ({ category }) => {
  const { t } = useTranslation();

  const colorClass = categoryColors[category] || 'bg-gray-100 text-gray-700 border-gray-200 dark:bg-graphite-surface-2 dark:text-graphite-muted dark:border-graphite-border';
  const translationKey = `dashboard.tileCategories.${category}`;
  const translatedCategory = t(translationKey);

  return (
    <span
      className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium border ${colorClass}`}
      data-testid={`category-badge-${category}`}
    >
      {translatedCategory}
    </span>
  );
};

export default CategoryBadge;
