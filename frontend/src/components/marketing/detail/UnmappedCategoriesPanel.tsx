import React from 'react';

interface UnmappedCategoriesPanelProps {
  categories: string[];
}

const CategoryPill: React.FC<{ name: string }> = ({ name }) => (
  <span
    data-testid="category-pill"
    className="inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium bg-amber-100 text-amber-900 border border-amber-300"
  >
    {name}
  </span>
);

export const UnmappedCategoriesPanel: React.FC<UnmappedCategoriesPanelProps> = ({
  categories,
}) => {
  return (
    <div
      role="status"
      aria-label="Nemapované kategorie z Outlooku"
      className="mt-4 p-4 border border-amber-300 bg-amber-50 rounded-md"
    >
      <h3 className="text-sm font-semibold text-amber-900">
        ⚠ Nemapované kategorie z Outlooku
      </h3>
      <p className="text-xs text-amber-800 mt-1">
        Tyto kategorie nebyly rozpoznány a události byly importovány jako výchozí
        kategorie (General). Doplňte je do appsettings.json →
        MarketingCalendar.CategoryMappings.
      </p>
      <div className="flex flex-wrap gap-2 mt-3">
        {categories.map((name) => (
          <CategoryPill key={name} name={name} />
        ))}
      </div>
    </div>
  );
};
