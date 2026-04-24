import React from "react";
import { X } from "lucide-react";

interface Filters {
  searchText: string;
  dateFrom: string;
  dateTo: string;
}

interface MarketingActionFiltersProps {
  filters: Filters;
  onChange: (filters: Filters) => void;
  onClear: () => void;
}

const EMPTY_FILTERS: Filters = { searchText: "", dateFrom: "", dateTo: "" };

const hasActiveFilters = (f: Filters) =>
  f.searchText !== "" || f.dateFrom !== "" || f.dateTo !== "";

const MarketingActionFilters: React.FC<MarketingActionFiltersProps> = ({
  filters,
  onChange,
  onClear,
}) => {
  const set =
    (key: keyof Filters) => (e: React.ChangeEvent<HTMLInputElement>) =>
      onChange({ ...filters, [key]: e.target.value });

  return (
    <div className="flex flex-wrap gap-3 items-center p-4 bg-white border border-gray-200 rounded-lg">
      <input
        type="text"
        placeholder="Hledat název..."
        value={filters.searchText}
        onChange={set("searchText")}
        className="border border-gray-300 rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500 min-w-[200px]"
      />
      <input
        type="date"
        value={filters.dateFrom}
        onChange={set("dateFrom")}
        className="border border-gray-300 rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500"
        title="Od"
      />
      <span className="text-gray-400 text-sm">–</span>
      <input
        type="date"
        value={filters.dateTo}
        onChange={set("dateTo")}
        className="border border-gray-300 rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500"
        title="Do"
      />
      {hasActiveFilters(filters) && (
        <button
          onClick={onClear}
          className="flex items-center gap-1 px-3 py-2 text-sm text-gray-600 hover:text-gray-900 border border-gray-300 rounded-md hover:bg-gray-50 transition-colors"
        >
          <X className="h-3 w-3" />
          Zrušit filtry
        </button>
      )}
    </div>
  );
};

export default MarketingActionFilters;
export { EMPTY_FILTERS };
export type { Filters as MarketingFilters };
