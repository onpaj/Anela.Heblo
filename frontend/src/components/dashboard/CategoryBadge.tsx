import React from 'react';
import { useTranslation } from 'react-i18next';

interface CategoryBadgeProps {
  category: string;
}

const categoryColors: Record<string, string> = {
  Manufacture: 'bg-blue-100 text-blue-700 border-blue-200',
  System: 'bg-gray-100 text-gray-700 border-gray-200',
  Warehouse: 'bg-green-100 text-green-700 border-green-200',
  Purchase: 'bg-purple-100 text-purple-700 border-purple-200',
  Finance: 'bg-yellow-100 text-yellow-700 border-yellow-200',
  Orders: 'bg-indigo-100 text-indigo-700 border-indigo-200',
  Error: 'bg-red-100 text-red-700 border-red-200',
};

const CategoryBadge: React.FC<CategoryBadgeProps> = ({ category }) => {
  const { t } = useTranslation();

  const colorClass = categoryColors[category] || 'bg-gray-100 text-gray-700 border-gray-200';
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
