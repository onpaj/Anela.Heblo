import React from "react";
import { Search } from "lucide-react";
import { GiftPackageFilters } from "./GiftPackageManufacturingList";
import { TimePeriod, resolveTimePeriod } from "../../../utils/timePeriod";

interface GiftPackageManufacturingFiltersProps {
  filters: GiftPackageFilters;
  onFilterChange: (newFilters: Partial<GiftPackageFilters>) => void;
}

const GiftPackageManufacturingFilters: React.FC<GiftPackageManufacturingFiltersProps> = ({
  filters,
  onFilterChange,
}) => {
  // Quick date range selectors
  const QUICK_DATE_RANGE_PERIODS: Record<
    "last12months" | "previousQuarter" | "nextQuarter",
    TimePeriod
  > = {
    last12months: TimePeriod.Y2Y,
    previousQuarter: TimePeriod.PreviousQuarter,
    nextQuarter: TimePeriod.FutureQuarter,
  };

  const handleQuickDateRange = (
    type: "last12months" | "previousQuarter" | "nextQuarter",
  ) => {
    const period = QUICK_DATE_RANGE_PERIODS[type];
    const { primary } = resolveTimePeriod(period);
    if (primary === null) return;
    onFilterChange({ fromDate: primary.from, toDate: primary.to });
  };

  // Get tooltip text for date range buttons
  const getDateRangeTooltip = (
    type: "last12months" | "previousQuarter" | "nextQuarter",
  ) => {
    const period = QUICK_DATE_RANGE_PERIODS[type];
    const { primary } = resolveTimePeriod(period);
    if (primary === null) return "";
    return `${primary.from.toLocaleDateString("cs-CZ")} - ${primary.to.toLocaleDateString("cs-CZ")}`;
  };

  return (
    <div>
      <h3 className="text-xs font-medium text-gray-700 dark:text-graphite-muted mb-2">Filtry</h3>
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-5 gap-3">
        {/* Search */}
        <div>
          <label className="block text-xs font-medium text-gray-700 dark:text-graphite-muted mb-1">
            Vyhledat
          </label>
          <div className="relative">
            <Search className="absolute left-2 top-1/2 transform -translate-y-1/2 h-3 w-3 text-gray-400 dark:text-graphite-faint" />
            <input
              type="text"
              value={filters.searchTerm || ""}
              onChange={(e) =>
                onFilterChange({ searchTerm: e.target.value })
              }
              placeholder="Kód, název balíčku..."
              className="pl-8 w-full border border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text dark:placeholder-graphite-faint rounded-md px-2 py-1.5 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-transparent"
            />
          </div>
        </div>

        {/* Date From */}
        <div>
          <label className="block text-xs font-medium text-gray-700 dark:text-graphite-muted mb-1">
            Od data
          </label>
          <input
            type="date"
            value={filters.fromDate?.toISOString().split("T")[0] || ""}
            onChange={(e) =>
              onFilterChange({ fromDate: new Date(e.target.value) })
            }
            className="w-full border border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text dark:placeholder-graphite-faint rounded-md px-2 py-1.5 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-transparent"
          />
        </div>

        {/* Date To */}
        <div>
          <label className="block text-xs font-medium text-gray-700 dark:text-graphite-muted mb-1">
            Do data
          </label>
          <input
            type="date"
            value={filters.toDate?.toISOString().split("T")[0] || ""}
            onChange={(e) =>
              onFilterChange({ toDate: new Date(e.target.value) })
            }
            className="w-full border border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text dark:placeholder-graphite-faint rounded-md px-2 py-1.5 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-transparent"
          />
        </div>

        {/* Sales Coefficient */}
        <div>
          <label className="block text-xs font-medium text-gray-700 dark:text-graphite-muted mb-1">
            Koeficient prodejů
          </label>
          <input
            type="number"
            min="0.1"
            max="5.0"
            step="0.1"
            value={filters.salesCoefficient?.toFixed(1) || "1.3"}
            onChange={(e) => {
              const value = parseFloat(e.target.value);
              if (!isNaN(value) && value >= 0.1 && value <= 5.0) {
                onFilterChange({ salesCoefficient: value });
              }
            }}
            className="w-full border border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text dark:placeholder-graphite-faint rounded-md px-2 py-1.5 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-transparent"
            title="Násobí prodejní data (0.1 - 5.0)"
          />
        </div>

        {/* Quick Date Range Selectors */}
        <div>
          <label className="block text-xs font-medium text-gray-700 dark:text-graphite-muted mb-1">
            Rychlé volby
          </label>
          <div className="space-y-1.5">
            <div className="flex gap-1">
              <button
                onClick={() => handleQuickDateRange("last12months")}
                className="px-1.5 py-0.5 text-xs bg-gray-100 dark:bg-graphite-surface-2 hover:bg-gray-200 dark:hover:bg-white/5 text-gray-700 dark:text-graphite-muted rounded border border-gray-300 dark:border-graphite-border transition-colors whitespace-nowrap"
                title={getDateRangeTooltip("last12months")}
              >
                Y2Y
              </button>
              <button
                onClick={() => handleQuickDateRange("previousQuarter")}
                className="px-1.5 py-0.5 text-xs bg-gray-100 dark:bg-graphite-surface-2 hover:bg-gray-200 dark:hover:bg-white/5 text-gray-700 dark:text-graphite-muted rounded border border-gray-300 dark:border-graphite-border transition-colors whitespace-nowrap"
                title={getDateRangeTooltip("previousQuarter")}
              >
                PrevQ
              </button>
              <button
                onClick={() => handleQuickDateRange("nextQuarter")}
                className="px-1.5 py-0.5 text-xs bg-gray-100 dark:bg-graphite-surface-2 hover:bg-gray-200 dark:hover:bg-white/5 text-gray-700 dark:text-graphite-muted rounded border border-gray-300 dark:border-graphite-border transition-colors whitespace-nowrap"
                title={getDateRangeTooltip("nextQuarter")}
              >
                NextQ
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
};

export default GiftPackageManufacturingFilters;