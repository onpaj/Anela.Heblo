import React, { useState } from "react";
import { Search, Filter, AlertCircle, Loader2 } from "lucide-react";
import {
  usePricingBaselineQuery,
  PricingBaselineFilter,
} from "../../api/hooks/usePricingSimulator";
import { ProductType } from "../../api/generated/api-client";
import { PAGE_CONTAINER_HEIGHT } from "../../constants/layout";
import { useScreenView } from "../../telemetry/useScreenView";
import PricingTotalsBar from "../pricing/PricingTotalsBar";
import PricingGrid from "../pricing/PricingGrid";

// Read-only screen: filter, sticky totals band and the product grid. Editing (cell
// overrides, recalculation, scenarios) is Task 9/10 and deliberately not wired here.
const PriceAnalysis: React.FC = () => {
  // Filter states - separate input values from applied filters, same shape as
  // ProductMarginsList so the two Finance screens behave consistently.
  const [productNameInput, setProductNameInput] = useState("");
  const [productCodeInput, setProductCodeInput] = useState("");
  const [productTypeInput, setProductTypeInput] = useState<string>("");
  const [productNameFilter, setProductNameFilter] = useState("");
  const [productCodeFilter, setProductCodeFilter] = useState("");
  const [productTypeFilter, setProductTypeFilter] = useState<string>("");

  useScreenView("Finance", "PriceAnalysis");

  const filter: PricingBaselineFilter = {
    productCode: productCodeFilter || undefined,
    productName: productNameFilter || undefined,
    productType: (productTypeFilter || undefined) as ProductType | undefined,
  };

  const { data, isLoading, error, refetch } = usePricingBaselineQuery(filter);

  const rows = data?.rows ?? [];
  const totals = data?.totals;

  const handleApplyFilters = async () => {
    setProductNameFilter(productNameInput);
    setProductCodeFilter(productCodeInput);
    setProductTypeFilter(productTypeInput);
    await refetch();
  };

  const handleKeyDown = (event: React.KeyboardEvent) => {
    if (event.key === "Enter") {
      handleApplyFilters();
    }
  };

  const handleClearFilters = async () => {
    setProductNameInput("");
    setProductCodeInput("");
    setProductTypeInput("");
    setProductNameFilter("");
    setProductCodeFilter("");
    setProductTypeFilter("");
    await refetch();
  };

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="flex items-center space-x-2">
          <Loader2 className="h-5 w-5 animate-spin text-indigo-500" />
          <div className="text-gray-500 dark:text-graphite-muted">
            Načítání analýzy cen...
          </div>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="flex items-center space-x-2 text-red-600 dark:text-red-400">
          <AlertCircle className="h-5 w-5" />
          <div>Chyba při načítání analýzy cen: {error.message}</div>
        </div>
      </div>
    );
  }

  return (
    <div
      className="flex flex-col w-full"
      style={{ height: PAGE_CONTAINER_HEIGHT }}
    >
      {/* Header - Fixed */}
      <div className="flex-shrink-0 mb-3">
        <h1 className="text-lg font-semibold text-gray-900 dark:text-graphite-text">
          Analýza cen
        </h1>
      </div>

      {/* Filters - Fixed */}
      <div className="flex-shrink-0 bg-white dark:bg-graphite-surface shadow dark:shadow-soft-dark rounded-lg p-4 mb-4">
        <div className="flex items-center justify-between flex-wrap gap-3">
          <div className="flex items-center gap-3 flex-1 min-w-0">
            <div className="flex items-center">
              <Filter className="h-4 w-4 text-gray-400 dark:text-graphite-faint mr-2" />
              <span className="text-sm font-medium text-gray-900 dark:text-graphite-text">
                Filtry:
              </span>
            </div>

            <div className="flex-1 max-w-xs">
              <div className="relative">
                <div className="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none">
                  <Search className="h-4 w-4 text-gray-400 dark:text-graphite-faint" />
                </div>
                <input
                  type="text"
                  id="productName"
                  value={productNameInput}
                  onChange={(e) => setProductNameInput(e.target.value)}
                  onKeyDown={handleKeyDown}
                  className="focus:ring-indigo-500 focus:border-indigo-500 block w-full pl-10 pr-3 py-2 sm:text-sm border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text dark:placeholder-graphite-faint rounded-md"
                  placeholder="Název produktu..."
                />
              </div>
            </div>

            <div className="flex-1 max-w-xs">
              <div className="relative">
                <div className="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none">
                  <Search className="h-4 w-4 text-gray-400 dark:text-graphite-faint" />
                </div>
                <input
                  type="text"
                  id="productCode"
                  value={productCodeInput}
                  onChange={(e) => setProductCodeInput(e.target.value)}
                  onKeyDown={handleKeyDown}
                  className="focus:ring-indigo-500 focus:border-indigo-500 block w-full pl-10 pr-3 py-2 sm:text-sm border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text dark:placeholder-graphite-faint rounded-md"
                  placeholder="Kód produktu..."
                />
              </div>
            </div>

            <div className="flex-1 max-w-xs">
              <select
                value={productTypeInput}
                onChange={(e) => {
                  setProductTypeInput(e.target.value);
                  setProductTypeFilter(e.target.value);
                }}
                className="focus:ring-indigo-500 focus:border-indigo-500 block w-full py-2 px-3 sm:text-sm border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text rounded-md"
              >
                <option value="">Výchozí (výrobky a zboží)</option>
                <option value="Product">Výrobky</option>
                <option value="Goods">Zboží</option>
                <option value="Material">Materiál</option>
                <option value="SemiProduct">Polotovar</option>
                <option value="Set">Dárkový balíček</option>
              </select>
            </div>
          </div>

          <div className="flex items-center gap-2">
            <button
              onClick={handleApplyFilters}
              className="bg-indigo-600 hover:bg-indigo-700 text-white font-medium py-2 px-4 rounded-md transition-colors duration-200 text-sm"
            >
              Filtrovat
            </button>
            <button
              onClick={handleClearFilters}
              className="bg-gray-500 hover:bg-gray-600 text-white font-medium py-2 px-3 rounded-md transition-colors duration-200 text-sm"
            >
              Vymazat
            </button>
          </div>
        </div>
      </div>

      {/* Totals band - sticky, stays visible while the grid below scrolls */}
      {totals && <PricingTotalsBar totals={totals} isRecalculating={false} />}

      {/* Product grid - every row, no pagination */}
      <PricingGrid rows={rows} editingDisabled />
    </div>
  );
};

export default PriceAnalysis;
