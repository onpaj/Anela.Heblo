import React from 'react';
import { useTranslation } from 'react-i18next';

interface SizeBadgeProps {
  size: string;
}

const sizeColors: Record<string, string> = {
  Small: 'bg-blue-100 text-blue-700 dark:bg-blue-900/30 dark:text-blue-300',
  Medium: 'bg-green-100 text-green-700 dark:bg-emerald-900/30 dark:text-emerald-300',
  Large: 'bg-purple-100 text-purple-700 dark:bg-purple-900/30 dark:text-purple-300',
};

const SizeBadge: React.FC<SizeBadgeProps> = ({ size }) => {
  const { t } = useTranslation();

  const colorClass = sizeColors[size] || 'bg-gray-100 text-gray-700 dark:bg-graphite-surface-2 dark:text-graphite-muted';
  const translationKey = `dashboard.tileSizes.${size}`;
  const translatedSize = t(translationKey);

  return (
    <span
      className={`px-2 py-0.5 rounded-full text-xs font-medium ${colorClass}`}
      data-testid={`size-badge-${size}`}
    >
      {translatedSize}
    </span>
  );
};

export default SizeBadge;
