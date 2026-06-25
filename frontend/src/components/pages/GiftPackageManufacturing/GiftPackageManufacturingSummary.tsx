import React from "react";
import {
  AlertTriangle,
  TrendingDown,
  CheckCircle,
  Package,
} from "lucide-react";
import { GiftPackageSeverity } from "../../../api/generated/api-client";
import { GiftPackageSummary, GiftPackageFilters } from "./GiftPackageManufacturingList";

interface GiftPackageManufacturingSummaryProps {
  summary: GiftPackageSummary;
  filters: GiftPackageFilters;
  onSeverityFilterClick: (severity: GiftPackageSeverity | "All") => void;
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
          className={`px-1 py-0.5 rounded transition-colors hover:bg-gray-100 dark:hover:bg-white/5 ${
            filters.severity === "All"
              ? "bg-gray-100 dark:bg-graphite-surface-2 ring-1 ring-gray-300 dark:ring-graphite-border"
              : ""
          }`}
          title="Všechny balíčky"
        >
          <span className="text-gray-700 dark:text-graphite-muted font-medium">
            {summary.totalPackages}
          </span>
        </button>
        <span className="text-gray-400 dark:text-graphite-faint">|</span>
        <button
          onClick={() => onSeverityFilterClick(GiftPackageSeverity.Critical)}
          className={`px-1 py-0.5 rounded transition-colors hover:bg-red-50 dark:hover:bg-red-900/30 ${
            filters.severity === GiftPackageSeverity.Critical
              ? "bg-red-50 dark:bg-red-900/30 ring-1 ring-red-300 dark:ring-red-700"
              : ""
          }`}
          title="Kritické zásoby"
        >
          <span className="text-red-600 dark:text-red-400 font-medium">
            {summary.criticalCount}
          </span>
        </button>
        <button
          onClick={() => onSeverityFilterClick(GiftPackageSeverity.Severe)}
          className={`px-1 py-0.5 rounded transition-colors hover:bg-orange-50 dark:hover:bg-orange-900/30 ${
            filters.severity === GiftPackageSeverity.Severe
              ? "bg-orange-50 dark:bg-orange-900/30 ring-1 ring-orange-300 dark:ring-orange-700"
              : ""
          }`}
          title="Vážné zásoby"
        >
          <span className="text-orange-600 dark:text-orange-400 font-medium">
            {summary.severeCount}
          </span>
        </button>
        <button
          onClick={() => onSeverityFilterClick(GiftPackageSeverity.Optimal)}
          className={`px-1 py-0.5 rounded transition-colors hover:bg-emerald-50 dark:hover:bg-emerald-900/30 ${
            filters.severity === GiftPackageSeverity.Optimal
              ? "bg-emerald-50 dark:bg-emerald-900/30 ring-1 ring-emerald-300 dark:ring-emerald-700"
              : ""
          }`}
          title="Optimální zásoby"
        >
          <span className="text-green-600 dark:text-emerald-400 font-medium">
            {summary.optimalCount}
          </span>
        </button>
      </div>
    );
  }

  return (
    <div>
      <h3 className="text-xs font-medium text-gray-700 dark:text-graphite-muted mb-2">
        Přehled stavů zásob
      </h3>
      <div className="flex flex-wrap items-center gap-2 text-xs">
        <button
          onClick={() => onSeverityFilterClick("All")}
          className={`flex items-center px-2 py-1 rounded-md transition-colors hover:bg-gray-100 dark:hover:bg-white/5 ${
            filters.severity === "All"
              ? "bg-gray-100 dark:bg-graphite-surface-2 ring-1 ring-gray-300 dark:ring-graphite-border"
              : ""
          }`}
        >
          <Package className="h-3 w-3 text-blue-500 dark:text-blue-400 mr-1" />
          <span className="text-gray-600 dark:text-graphite-muted">Celkem:</span>
          <span className="font-semibold text-gray-900 dark:text-graphite-text ml-1">
            {summary.totalPackages}
          </span>
        </button>

        <button
          onClick={() => onSeverityFilterClick(GiftPackageSeverity.Critical)}
          className={`flex items-center px-2 py-1 rounded-md transition-colors hover:bg-red-50 dark:hover:bg-red-900/30 ${
            filters.severity === GiftPackageSeverity.Critical
              ? "bg-red-50 dark:bg-red-900/30 ring-1 ring-red-300 dark:ring-red-700"
              : ""
          }`}
        >
          <AlertTriangle className="h-3 w-3 text-red-500 dark:text-red-400 mr-1" />
          <span className="text-gray-600 dark:text-graphite-muted">Kritické:</span>
          <span className="font-semibold text-red-600 dark:text-red-400 ml-1">
            {summary.criticalCount}
          </span>
        </button>

        <button
          onClick={() => onSeverityFilterClick(GiftPackageSeverity.Severe)}
          className={`flex items-center px-2 py-1 rounded-md transition-colors hover:bg-orange-50 dark:hover:bg-orange-900/30 ${
            filters.severity === GiftPackageSeverity.Severe
              ? "bg-orange-50 dark:bg-orange-900/30 ring-1 ring-orange-300 dark:ring-orange-700"
              : ""
          }`}
        >
          <TrendingDown className="h-3 w-3 text-orange-500 dark:text-orange-400 mr-1" />
          <span className="text-gray-600 dark:text-graphite-muted">Vážné:</span>
          <span className="font-semibold text-orange-600 dark:text-orange-400 ml-1">
            {summary.severeCount}
          </span>
        </button>

        <button
          onClick={() => onSeverityFilterClick(GiftPackageSeverity.Optimal)}
          className={`flex items-center px-2 py-1 rounded-md transition-colors hover:bg-emerald-50 dark:hover:bg-emerald-900/30 ${
            filters.severity === GiftPackageSeverity.Optimal
              ? "bg-emerald-50 dark:bg-emerald-900/30 ring-1 ring-emerald-300 dark:ring-emerald-700"
              : ""
          }`}
        >
          <CheckCircle className="h-3 w-3 text-green-500 dark:text-emerald-400 mr-1" />
          <span className="text-gray-600 dark:text-graphite-muted">Optimální:</span>
          <span className="font-semibold text-green-600 dark:text-emerald-400 ml-1">
            {summary.optimalCount}
          </span>
        </button>
      </div>
    </div>
  );
};

export default GiftPackageManufacturingSummary;