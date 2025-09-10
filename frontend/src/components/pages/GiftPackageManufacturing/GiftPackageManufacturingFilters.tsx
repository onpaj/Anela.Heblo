import React from "react";
import { Search } from "lucide-react";
import { GiftPackageFilters } from "./GiftPackageManufacturingList";

interface GiftPackageManufacturingFiltersProps {
  filters: GiftPackageFilters;
  onFilterChange: (newFilters: Partial<GiftPackageFilters>) => void;
}

const GiftPackageManufacturingFilters: React.FC<GiftPackageManufacturingFiltersProps> = ({
  filters,
  onFilterChange,
}) => {
  // Quick date range selectors
  const handleQuickDateRange = (
    type: "last12months" | "previousQuarter" | "nextQuarter",
  ) => {
    const now = new Date();
    let fromDate: Date;
    let toDate: Date;

    switch (type) {
      case "last12months":
        fromDate = new Date(
          now.getFullYear() - 1,
          now.getMonth(),
          now.getDate(),
        );
        toDate = new Date();
        break;

      case "previousQuarter":
        fromDate = new Date(now.getFullYear(), now.getMonth() - 3, 1);
        toDate = new Date(now.getFullYear(), now.getMonth(), 0);
        break;

      case "nextQuarter":
        const lastYear = now.getFullYear() - 1;
        fromDate = new Date(lastYear, now.getMonth(), 1);
        toDate = new Date(lastYear, now.getMonth() + 3, 0);
        break;

      default:
        return;
    }

    onFilterChange({ fromDate, toDate });
  };

  // Get tooltip text for date range buttons
  const getDateRangeTooltip = (
    type: "last12months" | "previousQuarter" | "nextQuarter",
  ) => {
    const now = new Date();
    let fromDate: Date;
    let toDate: Date;

    switch (type) {
      case "last12months":
        fromDate = new Date(
          now.getFullYear() - 1,
          now.getMonth(),
          now.getDate(),
        );
        toDate = new Date();
        break;

      case "previousQuarter":
        fromDate = new Date(now.getFullYear(), now.getMonth() - 3, 1);
        toDate = new Date(now.getFullYear(), now.getMonth(), 0);
        break;

      case "nextQuarter":
        const lastYear = now.getFullYear() - 1;
        fromDate = new Date(lastYear, now.getMonth(), 1);
        toDate = new Date(lastYear, now.getMonth() + 3, 0);
        break;

      default:
        return "";
    }

    return `${fromDate.toLocaleDateString("cs-CZ")} - ${toDate.toLocaleDateString("cs-CZ")}`;
  };

  return (
    <div>
      <h3 className="text-xs font-medium text-gray-700 mb-2">Filtry</h3>
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-3">
        {/* Search */}
        <div>
          <label className="block text-xs font-medium text-gray-700 mb-1">
            Vyhledat
          </label>
          <div className="relative">
            <Search className="absolute left-2 top-1/2 transform -translate-y-1/2 h-3 w-3 text-gray-400" />
            <input
              type="text"
              value={filters.searchTerm || ""}
              onChange={(e) =>
                onFilterChange({ searchTerm: e.target.value })
              }
              placeholder="Kód, název balíčku..."
              className="pl-8 w-full border border-gray-300 rounded-md px-2 py-1.5 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-transparent"
            />
          </div>
        </div>

        {/* Date From */}
        <div>
          <label className="block text-xs font-medium text-gray-700 mb-1">
            Od data
          </label>
          <input
            type="date"
            value={filters.fromDate?.toISOString().split("T")[0] || ""}
            onChange={(e) =>
              onFilterChange({ fromDate: new Date(e.target.value) })
            }
            className="w-full border border-gray-300 rounded-md px-2 py-1.5 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-transparent"
          />
        </div>

        {/* Date To */}
        <div>
          <label className="block text-xs font-medium text-gray-700 mb-1">
            Do data
          </label>
          <input
            type="date"
            value={filters.toDate?.toISOString().split("T")[0] || ""}
            onChange={(e) =>
              onFilterChange({ toDate: new Date(e.target.value) })
            }
            className="w-full border border-gray-300 rounded-md px-2 py-1.5 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-transparent"
          />
        </div>

        {/* Quick Date Range Selectors */}
        <div>
          <label className="block text-xs font-medium text-gray-700 mb-1">
            Rychlé volby
          </label>
          <div className="space-y-1.5">
            <div className="flex gap-1">
              <button
                onClick={() => handleQuickDateRange("last12months")}
                className="px-1.5 py-0.5 text-xs bg-gray-100 hover:bg-gray-200 text-gray-700 rounded border border-gray-300 transition-colors whitespace-nowrap"
                title={getDateRangeTooltip("last12months")}
              >
                Y2Y
              </button>
              <button
                onClick={() => handleQuickDateRange("previousQuarter")}
                className="px-1.5 py-0.5 text-xs bg-gray-100 hover:bg-gray-200 text-gray-700 rounded border border-gray-300 transition-colors whitespace-nowrap"
                title={getDateRangeTooltip("previousQuarter")}
              >
                PrevQ
              </button>
              <button
                onClick={() => handleQuickDateRange("nextQuarter")}
                className="px-1.5 py-0.5 text-xs bg-gray-100 hover:bg-gray-200 text-gray-700 rounded border border-gray-300 transition-colors whitespace-nowrap"
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