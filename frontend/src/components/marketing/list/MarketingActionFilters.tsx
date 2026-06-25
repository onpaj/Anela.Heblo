import React from "react";
import { X } from "lucide-react";
import { MarketingActionType } from "../../../api/generated/api-client";
import {
  ACTION_TYPE_LABELS,
  ALL_ACTION_TYPE_OPTIONS,
} from "./marketingActionTypeLabels";

export interface MarketingFilters {
  searchText: string;
  dateFrom: string;
  dateTo: string;
  actionType: MarketingActionType | "";
}

interface MarketingActionFiltersProps {
  filters: MarketingFilters;
  onChange: (filters: MarketingFilters) => void;
  onClear: () => void;
}

const EMPTY_FILTERS: MarketingFilters = {
  searchText: "",
  dateFrom: "",
  dateTo: "",
  actionType: "",
};

const hasActiveFilters = (f: MarketingFilters) =>
  f.searchText !== "" ||
  f.dateFrom !== "" ||
  f.dateTo !== "" ||
  f.actionType !== "";

const MarketingActionFilters: React.FC<MarketingActionFiltersProps> = ({
  filters,
  onChange,
  onClear,
}) => {
  const setText =
    (key: "searchText" | "dateFrom" | "dateTo") =>
    (e: React.ChangeEvent<HTMLInputElement>) =>
      onChange({ ...filters, [key]: e.target.value });

  const setActionType = (e: React.ChangeEvent<HTMLSelectElement>) =>
    onChange({
      ...filters,
      actionType: (e.target.value as MarketingActionType | ""),
    });

  return (
    <div className="flex flex-wrap gap-3 items-center p-4 bg-white dark:bg-graphite-surface border border-gray-200 dark:border-graphite-border rounded-lg">
      <select
        aria-label="Typ akce"
        value={filters.actionType}
        onChange={setActionType}
        className="border border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500"
      >
        <option value="">Všechny typy</option>
        {ALL_ACTION_TYPE_OPTIONS.map((t) => (
          <option key={t} value={t}>
            {ACTION_TYPE_LABELS[t]}
          </option>
        ))}
      </select>
      <input
        type="text"
        placeholder="Hledat název..."
        value={filters.searchText}
        onChange={setText("searchText")}
        className="border border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text dark:placeholder-graphite-faint rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500 min-w-[200px]"
      />
      <input
        type="date"
        value={filters.dateFrom}
        onChange={setText("dateFrom")}
        className="border border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text dark:placeholder-graphite-faint rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500"
        title="Od"
      />
      <span className="text-gray-400 dark:text-graphite-faint text-sm">–</span>
      <input
        type="date"
        value={filters.dateTo}
        onChange={setText("dateTo")}
        className="border border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text dark:placeholder-graphite-faint rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500"
        title="Do"
      />
      {hasActiveFilters(filters) && (
        <button
          onClick={onClear}
          className="flex items-center gap-1 px-3 py-2 text-sm text-gray-600 dark:text-graphite-muted hover:text-gray-900 border border-gray-300 dark:border-graphite-border rounded-md hover:bg-gray-50 dark:hover:bg-white/5 transition-colors"
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
