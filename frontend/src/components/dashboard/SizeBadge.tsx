import React from 'react';
import { useTranslation } from 'react-i18next';

interface SizeBadgeProps {
  size: string;
}

const sizeColors: Record<string, string> = {
  Small: 'bg-blue-100 text-blue-700',
  Medium: 'bg-green-100 text-green-700',
  Large: 'bg-purple-100 text-purple-700',
};

const SizeBadge: React.FC<SizeBadgeProps> = ({ size }) => {
  const { t } = useTranslation();

  const colorClass = sizeColors[size] || 'bg-gray-100 text-gray-700';
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
