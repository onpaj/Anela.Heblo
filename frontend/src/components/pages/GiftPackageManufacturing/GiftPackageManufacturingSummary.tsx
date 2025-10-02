import React from "react";
import {
  AlertTriangle,
  TrendingDown,
  CheckCircle,
  Package,
  Settings,
} from "lucide-react";
import { StockSeverity } from "../../../api/generated/api-client";
import { GiftPackageSummary, GiftPackageFilters } from "./GiftPackageManufacturingList";

interface GiftPackageManufacturingSummaryProps {
  summary: GiftPackageSummary;
  filters: GiftPackageFilters;
  onSeverityFilterClick: (severity: StockSeverity | "All") => void;
  compact?: boolean;
}

const GiftPackageManufacturingSummary: React.FC<GiftPackageManufacturingSummaryProps> = ({
  summary,
  filters,
  onSeverityFilterClick,
  compact = false,
}) => {
  if (compact) {
    return (
      <div className="flex items-center space-x-2 text-xs">
        <button
          onClick={() => onSeverityFilterClick("All")}
          className={`px-1 py-0.5 rounded transition-colors hover:bg-gray-100 ${
            filters.severity === "All"
              ? "bg-gray-100 ring-1 ring-gray-300"
              : ""
          }`}
          title="Všechny balíčky"
        >
          <span className="text-gray-700 font-medium">
            {summary.totalPackages}
          </span>
        </button>
        <span className="text-gray-400">|</span>
        <button
          onClick={() => onSeverityFilterClick(StockSeverity.Critical)}
          className={`px-1 py-0.5 rounded transition-colors hover:bg-red-50 ${
            filters.severity === StockSeverity.Critical
              ? "bg-red-50 ring-1 ring-red-300"
              : ""
          }`}
          title="Kritické zásoby"
        >
          <span className="text-red-600 font-medium">
            {summary.criticalCount}
          </span>
        </button>
        <button
          onClick={() => onSeverityFilterClick(StockSeverity.Severe)}
          className={`px-1 py-0.5 rounded transition-colors hover:bg-orange-50 ${
            filters.severity === StockSeverity.Severe
              ? "bg-orange-50 ring-1 ring-orange-300"
              : ""
          }`}
          title="Vážné zásoby"
        >
          <span className="text-orange-600 font-medium">
            {summary.severeCount}
          </span>
        </button>
        <button
          onClick={() => onSeverityFilterClick(StockSeverity.Low)}
          className={`px-1 py-0.5 rounded transition-colors hover:bg-amber-50 ${
            filters.severity === StockSeverity.Low
              ? "bg-amber-50 ring-1 ring-amber-300"
              : ""
          }`}
          title="Nízké zásoby"
        >
          <span className="text-amber-600 font-medium">
            {summary.lowStockCount}
          </span>
        </button>
        <button
          onClick={() => onSeverityFilterClick(StockSeverity.Optimal)}
          className={`px-1 py-0.5 rounded transition-colors hover:bg-emerald-50 ${
            filters.severity === StockSeverity.Optimal
              ? "bg-emerald-50 ring-1 ring-emerald-300"
              : ""
          }`}
          title="Optimální zásoby"
        >
          <span className="text-green-600 font-medium">
            {summary.optimalCount}
          </span>
        </button>
      </div>
    );
  }

  return (
    <div>
      <h3 className="text-xs font-medium text-gray-700 mb-2">
        Přehled stavů zásob
      </h3>
      <div className="flex flex-wrap items-center gap-2 text-xs">
        <button
          onClick={() => onSeverityFilterClick("All")}
          className={`flex items-center px-2 py-1 rounded-md transition-colors hover:bg-gray-100 ${
            filters.severity === "All"
              ? "bg-gray-100 ring-1 ring-gray-300"
              : ""
          }`}
        >
          <Package className="h-3 w-3 text-blue-500 mr-1" />
          <span className="text-gray-600">Celkem:</span>
          <span className="font-semibold text-gray-900 ml-1">
            {summary.totalPackages}
          </span>
        </button>

        <button
          onClick={() => onSeverityFilterClick(StockSeverity.Critical)}
          className={`flex items-center px-2 py-1 rounded-md transition-colors hover:bg-red-50 ${
            filters.severity === StockSeverity.Critical
              ? "bg-red-50 ring-1 ring-red-300"
              : ""
          }`}
        >
          <AlertTriangle className="h-3 w-3 text-red-500 mr-1" />
          <span className="text-gray-600">Kritické:</span>
          <span className="font-semibold text-red-600 ml-1">
            {summary.criticalCount}
          </span>
        </button>

        <button
          onClick={() => onSeverityFilterClick(StockSeverity.Severe)}
          className={`flex items-center px-2 py-1 rounded-md transition-colors hover:bg-orange-50 ${
            filters.severity === StockSeverity.Severe
              ? "bg-orange-50 ring-1 ring-orange-300"
              : ""
          }`}
        >
          <TrendingDown className="h-3 w-3 text-orange-500 mr-1" />
          <span className="text-gray-600">Vážné:</span>
          <span className="font-semibold text-orange-600 ml-1">
            {summary.severeCount}
          </span>
        </button>

        <button
          onClick={() => onSeverityFilterClick(StockSeverity.Low)}
          className={`flex items-center px-2 py-1 rounded-md transition-colors hover:bg-amber-50 ${
            filters.severity === StockSeverity.Low
              ? "bg-amber-50 ring-1 ring-amber-300"
              : ""
          }`}
        >
          <TrendingDown className="h-3 w-3 text-amber-500 mr-1" />
          <span className="text-gray-600">Nízké:</span>
          <span className="font-semibold text-amber-600 ml-1">
            {summary.lowStockCount}
          </span>
        </button>

        <button
          onClick={() => onSeverityFilterClick(StockSeverity.Optimal)}
          className={`flex items-center px-2 py-1 rounded-md transition-colors hover:bg-emerald-50 ${
            filters.severity === StockSeverity.Optimal
              ? "bg-emerald-50 ring-1 ring-emerald-300"
              : ""
          }`}
        >
          <CheckCircle className="h-3 w-3 text-green-500 mr-1" />
          <span className="text-gray-600">Optimální:</span>
          <span className="font-semibold text-green-600 ml-1">
            {summary.optimalCount}
          </span>
        </button>

        <button
          onClick={() => onSeverityFilterClick(StockSeverity.Overstocked)}
          className={`flex items-center px-2 py-1 rounded-md transition-colors hover:bg-blue-50 ${
            filters.severity === StockSeverity.Overstocked
              ? "bg-blue-50 ring-1 ring-blue-300"
              : ""
          }`}
        >
          <Package className="h-3 w-3 text-blue-500 mr-1" />
          <span className="text-gray-600">Přeskladněno:</span>
          <span className="font-semibold text-blue-600 ml-1">
            {summary.overstockedCount}
          </span>
        </button>

        <button
          onClick={() => onSeverityFilterClick(StockSeverity.NotConfigured)}
          className={`flex items-center px-2 py-1 rounded-md transition-colors hover:bg-gray-50 ${
            filters.severity === StockSeverity.NotConfigured
              ? "bg-gray-50 ring-1 ring-gray-300"
              : ""
          }`}
        >
          <Settings className="h-3 w-3 text-gray-500 mr-1" />
          <span className="text-gray-600">Nezkonfigurováno:</span>
          <span className="font-semibold text-gray-600 ml-1">
            {summary.notConfiguredCount}
          </span>
        </button>
      </div>
    </div>
  );
};

export default GiftPackageManufacturingSummary;