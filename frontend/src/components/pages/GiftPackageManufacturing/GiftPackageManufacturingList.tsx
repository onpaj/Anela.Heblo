import React, { useState, useMemo } from "react";
import {
  Search,
  RefreshCw,
  Download,
  AlertTriangle,
  Package,
  ChevronLeft,
  ChevronRight,
  ChevronUp,
  ChevronDown,
  HelpCircle,
  Info,
} from "lucide-react";
import { useSearchParams } from "react-router-dom";
import { useAvailableGiftPackages } from "../../../api/hooks/useGiftPackageManufacturing";
import { GiftPackageSeverity } from "../../../api/generated/api-client";
import { PAGE_CONTAINER_HEIGHT } from "../../../constants/layout";
import GiftPackageManufacturingFilters from "./GiftPackageManufacturingFilters";
import GiftPackageManufacturingSummary from "./GiftPackageManufacturingSummary";

// Filter types
interface GiftPackageFilters {
  fromDate: Date;
  toDate: Date;
  searchTerm: string;
  severity: GiftPackageSeverity | "All";
  pageNumber: number;
  pageSize: number;
  sortBy: GiftPackageSortBy;
  sortDescending: boolean;
  salesCoefficient: number;
}

enum GiftPackageSortBy {
  ProductCode = "ProductCode",
  ProductName = "ProductName",
  AvailableStock = "AvailableStock",
  DailySales = "DailySales",
  SuggestedQuantity = "SuggestedQuantity",
  OverstockOptimal = "OverstockOptimal",
  OverstockMinimal = "OverstockMinimal",
  StockCoveragePercent = "StockCoveragePercent",
  Severity = "Severity",
}

interface GiftPackage {
  code: string;
  name: string;
  availableStock: number;
  dailySales: number;
  suggestedQuantity: number;
  severity: GiftPackageSeverity;
  overstockOptimal: number;
  overstockMinimal: number;
  stockCoveragePercent: number;
}

interface GiftPackageSummary {
  totalPackages: number;
  criticalCount: number;
  severeCount: number;
  optimalCount: number;
}

interface GiftPackageManufacturingListProps {
  onPackageClick: (pkg: GiftPackage) => void;
  onCatalogDetailClick: (productCode: string) => void;
  onSalesCoefficientChange: (coefficient: number) => void;
  onDateFilterChange: (fromDate: Date, toDate: Date) => void;
}

const GiftPackageManufacturingList: React.FC<GiftPackageManufacturingListProps> = ({
  onPackageClick,
  onCatalogDetailClick,
  onSalesCoefficientChange,
  onDateFilterChange,
}) => {
  const [searchParams] = useSearchParams();
  
  // State for filters - initialize from URL parameters
  const [filters, setFilters] = useState<GiftPackageFilters>(() => {
    const severityParam = searchParams.get('severity');

    // Map URL parameter directly to GiftPackageSeverity enum or "All"
    let severityValue: GiftPackageSeverity | "All" = "All";
    if (severityParam && Object.values(GiftPackageSeverity).includes(severityParam as GiftPackageSeverity)) {
      severityValue = severityParam as GiftPackageSeverity;
    }
    
    return {
      fromDate: new Date(
        new Date().getFullYear() - 1,
        new Date().getMonth(),
        new Date().getDate(),
      ),
      toDate: new Date(),
      searchTerm: searchParams.get('searchTerm') || "",
      severity: severityValue,
      pageNumber: 1,
      pageSize: 20,
      sortBy: GiftPackageSortBy.Severity,
      sortDescending: false,
      salesCoefficient: 1.3,
    };
  });

  // State for collapsible sections
  const [isControlsCollapsed, setIsControlsCollapsed] = useState(false);
  
  // Load gift package data from backend with date parameters
  const { data: giftPackageData, isLoading: giftPackageLoading, error: giftPackageError, refetch } = useAvailableGiftPackages({
    fromDate: filters.fromDate,
    toDate: filters.toDate,
    salesCoefficient: filters.salesCoefficient
  });

  // Process gift package data - now using server-calculated values
  const { giftPackages, summary } = useMemo(() => {
    if (!giftPackageData?.giftPackages) return { giftPackages: [], summary: null };
    
    const packages: GiftPackage[] = giftPackageData.giftPackages.map(pkg => ({
      code: pkg.code!,
      name: pkg.name!,
      availableStock: pkg.availableStock ?? 0,
      dailySales: pkg.dailySales ?? 0,
      suggestedQuantity: pkg.suggestedQuantity ?? 0,
      severity: pkg.severity ?? GiftPackageSeverity.Optimal,
      overstockOptimal: pkg.overstockOptimal ?? 0,
      overstockMinimal: pkg.overstockMinimal ?? 0,
      stockCoveragePercent: pkg.stockCoveragePercent ?? 0,
    }));
    
    // Calculate summary
    const summaryData: GiftPackageSummary = {
      totalPackages: packages.length,
      criticalCount: packages.filter(p => p.severity === GiftPackageSeverity.Critical).length,
      severeCount: packages.filter(p => p.severity === GiftPackageSeverity.Severe).length,
      optimalCount: packages.filter(p => p.severity === GiftPackageSeverity.Optimal).length,
    };
    
    return { giftPackages: packages, summary: summaryData };
  }, [giftPackageData?.giftPackages]);
  
  // Apply filters, sorting, and pagination
  const { filteredPackages, totalCount, totalPages } = useMemo(() => {
    let filtered = giftPackages;
    
    // Apply search filter
    if (filters.searchTerm.trim()) {
      const term = filters.searchTerm.toLowerCase();
      filtered = filtered.filter(pkg => 
        pkg.name.toLowerCase().includes(term) || 
        pkg.code.toLowerCase().includes(term)
      );
    }
    
    // Apply severity filter
    if (filters.severity !== "All") {
      filtered = filtered.filter(pkg => pkg.severity === filters.severity);
    }
    
    // Apply sorting
    filtered.sort((a, b) => {
      // Helper function to get severity order
      const getSeverityOrder = (severity: GiftPackageSeverity) => {
        switch (severity) {
          case GiftPackageSeverity.Critical: return 0;
          case GiftPackageSeverity.Severe: return 1;
          case GiftPackageSeverity.Optimal: return 2;
          default: return 3;
        }
      };

      // Default sorting by Severity first, then by NS% ascending
      if (filters.sortBy === GiftPackageSortBy.Severity || filters.sortBy === GiftPackageSortBy.SuggestedQuantity) {
        // Primary sort by severity (Critical -> Severe -> Low -> Optimal)
        const severityA = getSeverityOrder(a.severity);
        const severityB = getSeverityOrder(b.severity);
        
        if (severityA !== severityB) {
          return filters.sortDescending ? severityB - severityA : severityA - severityB;
        }
        
        // Secondary sort by NS% ascending (lower percentages = more urgent)
        const nsA = a.stockCoveragePercent || 0;
        const nsB = b.stockCoveragePercent || 0;
        return nsA - nsB;
      }

      // Standard sorting for other columns
      let aValue: any, bValue: any;
      
      switch (filters.sortBy) {
        case GiftPackageSortBy.ProductCode:
          aValue = a.code;
          bValue = b.code;
          break;
        case GiftPackageSortBy.ProductName:
          aValue = a.name;
          bValue = b.name;
          break;
        case GiftPackageSortBy.AvailableStock:
          aValue = a.availableStock;
          bValue = b.availableStock;
          break;
        case GiftPackageSortBy.DailySales:
          aValue = a.dailySales;
          bValue = b.dailySales;
          break;
        case GiftPackageSortBy.StockCoveragePercent:
          aValue = a.stockCoveragePercent;
          bValue = b.stockCoveragePercent;
          break;
        case GiftPackageSortBy.OverstockOptimal:
          aValue = a.overstockOptimal;
          bValue = b.overstockOptimal;
          break;
        case GiftPackageSortBy.OverstockMinimal:
          aValue = a.overstockMinimal;
          bValue = b.overstockMinimal;
          break;
        default:
          aValue = a.code;
          bValue = b.code;
      }
      
      if (typeof aValue === 'string' && typeof bValue === 'string') {
        const comparison = aValue.localeCompare(bValue);
        return filters.sortDescending ? -comparison : comparison;
      } else {
        const comparison = (aValue || 0) - (bValue || 0);
        return filters.sortDescending ? -comparison : comparison;
      }
    });
    
    const total = filtered.length;
    const pages = Math.ceil(total / filters.pageSize);
    
    // Apply pagination
    const startIndex = (filters.pageNumber - 1) * filters.pageSize;
    const paginatedPackages = filtered.slice(startIndex, startIndex + filters.pageSize);
    
    return { 
      filteredPackages: paginatedPackages, 
      totalCount: total, 
      totalPages: pages 
    };
  }, [giftPackages, filters]);

  // Handler for filter changes
  const handleFilterChange = (newFilters: Partial<GiftPackageFilters>) => {
    setFilters((prev) => ({ ...prev, ...newFilters, pageNumber: 1 }));
    
    // Propagate sales coefficient changes to parent
    if (newFilters.salesCoefficient !== undefined) {
      onSalesCoefficientChange(newFilters.salesCoefficient);
    }
    
    // Propagate date filter changes to parent
    if (newFilters.fromDate !== undefined || newFilters.toDate !== undefined) {
      const currentFilters = filters;
      const updatedFromDate = newFilters.fromDate ?? currentFilters.fromDate;
      const updatedToDate = newFilters.toDate ?? currentFilters.toDate;
      onDateFilterChange(updatedFromDate, updatedToDate);
    }
  };

  // Handler for pagination
  const handlePageChange = (newPage: number) => {
    if (newPage >= 1 && newPage <= totalPages) {
      setFilters((prev) => ({ ...prev, pageNumber: newPage }));
    }
  };

  // Handler for page size change
  const handlePageSizeChange = (newPageSize: number) => {
    setFilters((prev) => ({ ...prev, pageSize: newPageSize, pageNumber: 1 }));
  };

  // Handler for sorting
  const handleSort = (column: GiftPackageSortBy) => {
    setFilters((prev) => ({
      ...prev,
      sortBy: column,
      sortDescending: prev.sortBy === column ? !prev.sortDescending : true,
      pageNumber: 1,
    }));
  };

  // Export functionality (placeholder)
  const handleExport = () => {};

  // Handle severity filter click from summary cards
  const handleSeverityFilterClick = (severity: GiftPackageSeverity | "All") => {
    handleFilterChange({ severity });
  };

  // Sortable header component
  const SortableHeader: React.FC<{
    column: GiftPackageSortBy;
    children: React.ReactNode;
    className?: string;
  }> = ({ column, children, className = "" }) => {
    const isActive = filters.sortBy === column;
    const isAscending = isActive && !filters.sortDescending;
    const isDescending = isActive && filters.sortDescending;

    return (
      <th
        scope="col"
        className={`px-6 py-3 text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider cursor-pointer hover:bg-gray-100 dark:hover:bg-white/5 select-none ${className}`}
        onClick={() => handleSort(column)}
      >
        <div className="flex items-center space-x-1">
          <span>{children}</span>
          <div className="flex flex-col">
            <ChevronUp
              className={`h-3 w-3 ${isAscending ? "text-indigo-600 dark:text-graphite-accent" : "text-gray-300 dark:text-graphite-faint"}`}
            />
            <ChevronDown
              className={`h-3 w-3 -mt-1 ${isDescending ? "text-indigo-600 dark:text-graphite-accent" : "text-gray-300 dark:text-graphite-faint"}`}
            />
          </div>
        </div>
      </th>
    );
  };

  // Get row background color based on severity
  const getRowColorClass = (severity: GiftPackageSeverity) => {
    switch (severity) {
      case GiftPackageSeverity.Critical:
        return "bg-red-50/30 hover:bg-red-50/50 dark:bg-red-900/10 dark:hover:bg-red-900/20";
      case GiftPackageSeverity.Severe:
        return "bg-orange-50/30 hover:bg-orange-50/50 dark:bg-orange-900/10 dark:hover:bg-orange-900/20";
      case GiftPackageSeverity.Optimal:
        return "bg-emerald-50/30 hover:bg-emerald-50/50 dark:bg-emerald-900/10 dark:hover:bg-emerald-900/20";
      default:
        return "hover:bg-gray-50 dark:hover:bg-white/5";
    }
  };
  
  // Get color strip for severity
  const getSeverityStripColor = (severity: GiftPackageSeverity) => {
    if (filters.severity !== "All") {
      return "";
    }

    switch (severity) {
      case GiftPackageSeverity.Critical:
        return "bg-red-500";
      case GiftPackageSeverity.Severe:
        return "bg-orange-500";
      case GiftPackageSeverity.Optimal:
        return "bg-emerald-500";
      default:
        return "";
    }
  };
  
  const isLoading = giftPackageLoading;
  const error = giftPackageError;
  
  if (error) {
    return (
      <div className="min-h-screen bg-gray-50 dark:bg-graphite-surface-2 px-4 py-8">
        <div className="max-w-7xl mx-auto">
          <div className="bg-red-50 dark:bg-red-900/30 border border-red-200 dark:border-graphite-border rounded-lg p-6">
            <div className="flex items-center">
              <AlertTriangle className="h-5 w-5 text-red-400 dark:text-red-400 mr-2" />
              <h3 className="text-lg font-medium text-red-800 dark:text-red-300">
                Chyba při načítání dat
              </h3>
            </div>
            <p className="mt-2 text-sm text-red-700 dark:text-red-300">
              {error instanceof Error ? error.message : "Neočekávaná chyba"}
            </p>
            <button
              onClick={() => refetch()}
              className="mt-4 bg-red-100 dark:bg-red-900/30 hover:bg-red-200 dark:hover:bg-red-900/50 text-red-800 dark:text-red-300 px-4 py-2 rounded-md text-sm font-medium"
            >
              Zkusit znovu
            </button>
          </div>
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
          Výroba dárkových balíčků
        </h1>
      </div>

      {/* Controls - Single Collapsible Block */}
      <div className="flex-shrink-0 bg-white dark:bg-graphite-surface rounded-lg shadow dark:shadow-soft-dark mb-4">
        <div className="p-3 border-b border-gray-200 dark:border-graphite-border">
          <div className="flex items-center justify-between">
            <button
              onClick={() => setIsControlsCollapsed(!isControlsCollapsed)}
              className="flex items-center space-x-2 text-sm font-medium text-gray-900 dark:text-graphite-text hover:text-gray-700"
            >
              {isControlsCollapsed ? (
                <ChevronRight className="h-4 w-4" />
              ) : (
                <ChevronDown className="h-4 w-4" />
              )}
              <span>Filtry a nastavení</span>
              {summary && (
                <span className="text-xs text-gray-500 dark:text-graphite-muted">
                  ({summary.totalPackages} balíčků)
                </span>
              )}
            </button>

            <div className="flex items-center space-x-3">
              {/* Always visible controls when collapsed */}
              {isControlsCollapsed && (
                <>
                  {/* Quick summary when collapsed - clickable */}
                  {summary && (
                    <GiftPackageManufacturingSummary
                      summary={summary}
                      filters={filters}
                      onSeverityFilterClick={handleSeverityFilterClick}
                      compact={true}
                    />
                  )}
                  {/* Search field when collapsed */}
                  <div className="flex-1 max-w-xs">
                    <div className="relative">
                      <Search className="absolute left-2 top-1/2 transform -translate-y-1/2 h-3 w-3 text-gray-400 dark:text-graphite-faint" />
                      <input
                        type="text"
                        value={filters.searchTerm || ""}
                        onChange={(e) =>
                          handleFilterChange({ searchTerm: e.target.value })
                        }
                        placeholder="Vyhledat..."
                        className="pl-7 w-full border border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text dark:placeholder-graphite-faint rounded-md px-2 py-1 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-transparent"
                      />
                    </div>
                  </div>
                </>
              )}

              {/* Action buttons - always visible */}
              <button
                onClick={() => refetch()}
                disabled={giftPackageLoading}
                className="flex items-center px-2 py-1 border border-gray-300 dark:border-graphite-border rounded-md shadow-sm dark:shadow-soft-dark text-xs font-medium text-gray-700 dark:text-graphite-muted bg-white dark:bg-graphite-surface hover:bg-gray-50 dark:hover:bg-white/5 disabled:opacity-50"
              >
                <RefreshCw
                  className={`h-3 w-3 mr-1 ${giftPackageLoading ? "animate-spin" : ""}`}
                />
                {isControlsCollapsed ? "" : "Obnovit"}
              </button>
              <button
                onClick={handleExport}
                className="flex items-center px-2 py-1 border border-gray-300 dark:border-graphite-border rounded-md shadow-sm dark:shadow-soft-dark text-xs font-medium text-gray-700 dark:text-graphite-muted bg-white dark:bg-graphite-surface hover:bg-gray-50 dark:hover:bg-white/5"
              >
                <Download className="h-3 w-3 mr-1" />
                {isControlsCollapsed ? "" : "Export"}
              </button>

              {/* Help */}
              <div className="relative group">
                <HelpCircle className="h-4 w-4 text-gray-400 dark:text-graphite-faint cursor-help" />
                <div className="absolute right-0 top-6 w-80 bg-gray-900 text-white text-xs rounded-lg p-3 opacity-0 group-hover:opacity-100 transition-opacity z-10 pointer-events-none">
                  <div className="space-y-2">
                    <div className="flex items-start">
                      <div className="w-3 h-3 bg-red-200 rounded-sm mr-2 mt-0.5 flex-shrink-0"></div>
                      <div>
                        <span className="font-medium text-red-200">
                          Kritické:
                        </span>{" "}
                        Zásoby pod minimem
                      </div>
                    </div>
                    <div className="flex items-start">
                      <div className="w-3 h-3 bg-orange-200 rounded-sm mr-2 mt-0.5 flex-shrink-0"></div>
                      <div>
                        <span className="font-medium text-orange-200">
                          Vážné:
                        </span>{" "}
                        Potřeba výroby
                      </div>
                    </div>
                    <div className="flex items-start">
                      <div className="w-3 h-3 bg-emerald-200 rounded-sm mr-2 mt-0.5 flex-shrink-0"></div>
                      <div>
                        <span className="font-medium text-emerald-200">
                          Optimální:
                        </span>{" "}
                        Zásoby jsou v pořádku
                      </div>
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>

        {!isControlsCollapsed && (
          <div className="p-3 space-y-4">
            {/* Summary Cards */}
            {summary && (
              <GiftPackageManufacturingSummary
                summary={summary}
                filters={filters}
                onSeverityFilterClick={handleSeverityFilterClick}
                compact={false}
              />
            )}

            {/* Filters */}
            <GiftPackageManufacturingFilters
              filters={filters}
              onFilterChange={handleFilterChange}
            />
          </div>
        )}
      </div>

      {/* Results Table */}
      <div className="flex-1 bg-white dark:bg-graphite-surface rounded-lg shadow dark:shadow-soft-dark overflow-hidden flex flex-col min-h-0">
        {isLoading ? (
          <div className="flex items-center justify-center py-12">
            <RefreshCw className="h-8 w-8 animate-spin text-gray-400 dark:text-graphite-faint" />
            <span className="ml-2 text-gray-600 dark:text-graphite-muted">Načítání balíčků...</span>
          </div>
        ) : filteredPackages.length === 0 ? (
          <div className="flex items-center justify-center py-12">
            <div className="text-center">
              <Package className="h-12 w-12 text-gray-400 dark:text-graphite-faint mx-auto mb-4" />
              <h3 className="text-lg font-medium text-gray-900 dark:text-graphite-text mb-2">
                Žádné výsledky
              </h3>
              <p className="text-gray-600 dark:text-graphite-muted">
                Zkuste upravit filtry nebo vyhledávací kritéria.
              </p>
            </div>
          </div>
        ) : (
          <div className="flex-1 overflow-auto">
            <table className="min-w-full divide-y divide-gray-200 dark:divide-graphite-border">
              <thead className="bg-gray-50 dark:bg-graphite-surface-2 sticky top-0 z-10">
                <tr>
                  <SortableHeader
                    column={GiftPackageSortBy.ProductCode}
                    className="text-left w-40"
                  >
                    Balíček
                  </SortableHeader>
                  <SortableHeader
                    column={GiftPackageSortBy.AvailableStock}
                    className="text-right"
                  >
                    Skladem
                  </SortableHeader>
                  
                  <SortableHeader
                    column={GiftPackageSortBy.SuggestedQuantity}
                    className="text-right"
                  >
                    Doporučeno
                  </SortableHeader>
                  <SortableHeader
                    column={GiftPackageSortBy.StockCoveragePercent}
                    className="text-right"
                  >
                    NS%
                  </SortableHeader>
                  <SortableHeader
                    column={GiftPackageSortBy.DailySales}
                    className="text-right hidden md:table-cell"
                  >
                    Prodeje/den
                  </SortableHeader>
                  <SortableHeader
                    column={GiftPackageSortBy.OverstockOptimal}
                    className="text-right"
                  >
                    NS dní
                  </SortableHeader>
                   <SortableHeader
                    column={GiftPackageSortBy.OverstockMinimal}
                    className="text-right"
                  >
                    NS min
                  </SortableHeader>
                </tr>
              </thead>
              <tbody className="bg-white dark:bg-graphite-surface divide-y divide-gray-200 dark:divide-graphite-border">
                {filteredPackages.map((pkg) => (
                  <tr
                    key={pkg.code}
                    className={`${getRowColorClass(pkg.severity)} hover:bg-gray-50 dark:hover:bg-white/5 cursor-pointer transition-colors duration-150`}
                    onClick={() => onPackageClick(pkg)}
                    title="Klikněte pro zobrazení komponent balíčku a výrobu"
                  >
                    {/* Package Info */}
                    <td className="px-6 py-4 whitespace-nowrap w-40">
                      <div className="flex items-center">
                        {/* Color strip based on severity */}
                        {getSeverityStripColor(pkg.severity) && (
                          <div
                            className={`w-1 h-8 mr-3 rounded-sm ${getSeverityStripColor(pkg.severity)}`}
                          ></div>
                        )}
                        <div className="flex-1 min-w-0">
                          <div className="flex items-center gap-2">
                            <div className="text-sm text-gray-900 dark:text-graphite-text truncate">
                              {pkg.name}
                            </div>
                            <button
                              onClick={(e) => {
                                e.stopPropagation();
                                onCatalogDetailClick(pkg.code);
                              }}
                              className="flex-shrink-0 text-gray-400 dark:text-graphite-faint hover:text-indigo-600 dark:hover:text-graphite-accent transition-colors"
                              title="Zobrazit detail produktu"
                            >
                              <Info className="h-4 w-4" />
                            </button>
                          </div>
                          <div className="text-xs text-gray-500 dark:text-graphite-muted">
                            {pkg.code}
                          </div>
                        </div>
                      </div>
                    </td>

                    {/* Available Stock */}
                    <td className="px-6 py-4 whitespace-nowrap text-right text-xs text-gray-900 dark:text-graphite-text">
                      <div className="font-bold">
                        {pkg.availableStock.toFixed(0)}
                      </div>
                    </td>
                    {/* Suggested Quantity */}
                    <td className="px-6 py-4 whitespace-nowrap text-right text-xs text-gray-900 dark:text-graphite-text">
                      <div className={`font-bold ${
                        pkg.suggestedQuantity > 0 ? "text-orange-600 dark:text-orange-400" : "text-green-600 dark:text-emerald-400"
                      }`}>
                        {pkg.suggestedQuantity}
                      </div>
                      <div className="text-xs text-gray-500 dark:text-graphite-muted md:hidden">
                        {pkg.dailySales.toFixed(1)}/den
                      </div>
                    </td>
                    {/* NS% (StockCoveragePercent) */}
                    <td className="px-6 py-4 whitespace-nowrap text-right text-xs text-gray-900 dark:text-graphite-text">
                      <div className={`font-bold ${
                        pkg.stockCoveragePercent >= 100 ? "text-green-600 dark:text-emerald-400" :
                        pkg.stockCoveragePercent >= 50 ? "text-orange-600 dark:text-orange-400" : "text-red-600 dark:text-red-400"
                      }`}>
                        {pkg.stockCoveragePercent.toFixed(0)}%
                      </div>
                    </td>
                    {/* Daily Sales - Hidden on mobile */}
                    <td className="px-6 py-4 whitespace-nowrap text-right text-xs text-gray-900 dark:text-graphite-text hidden md:table-cell">
                      <div>{pkg.dailySales.toFixed(1)}</div>
                      <div className="text-xs text-gray-500 dark:text-graphite-muted md:hidden">
                        {pkg.dailySales.toFixed(1)}/den
                      </div>
                    </td>
                    {/* NS dní (OverstockOptimal) */}
                    <td className="px-6 py-4 whitespace-nowrap text-right text-xs text-gray-900 dark:text-graphite-text">
                      <div className="font-bold">
                        {pkg.overstockOptimal}
                      </div>
                    </td>
                    {/* NS min (OverstockMinimal) */}
                    <td className="px-6 py-4 whitespace-nowrap text-right text-xs text-gray-900 dark:text-graphite-text">
                      <div className="font-bold">
                        {pkg.overstockMinimal}
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {/* Pagination - Compact */}
        {totalCount > 0 && (
          <div className="flex-shrink-0 bg-white dark:bg-graphite-surface px-3 py-2 flex items-center justify-between border-t border-gray-200 dark:border-graphite-border text-xs">
            <div className="flex-1 flex justify-between sm:hidden">
              <button
                onClick={() => handlePageChange(filters.pageNumber - 1)}
                disabled={filters.pageNumber <= 1}
                className="relative inline-flex items-center px-2 py-1 border border-gray-300 dark:border-graphite-border text-xs font-medium rounded text-gray-700 dark:text-graphite-muted bg-white dark:bg-graphite-surface hover:bg-gray-50 dark:hover:bg-white/5 disabled:opacity-50 disabled:cursor-not-allowed"
              >
                Předchozí
              </button>
              <button
                onClick={() => handlePageChange(filters.pageNumber + 1)}
                disabled={filters.pageNumber >= totalPages}
                className="ml-2 relative inline-flex items-center px-2 py-1 border border-gray-300 dark:border-graphite-border text-xs font-medium rounded text-gray-700 dark:text-graphite-muted bg-white dark:bg-graphite-surface hover:bg-gray-50 dark:hover:bg-white/5 disabled:opacity-50 disabled:cursor-not-allowed"
              >
                Další
              </button>
            </div>
            <div className="hidden sm:flex-1 sm:flex sm:items-center sm:justify-between">
              <div className="flex items-center space-x-3">
                <p className="text-xs text-gray-600 dark:text-graphite-muted">
                  {(filters.pageNumber - 1) * filters.pageSize + 1}-
                  {Math.min(
                    filters.pageNumber * filters.pageSize,
                    totalCount,
                  )}{" "}
                  z {totalCount}
                </p>
                <div className="flex items-center space-x-1">
                  <span className="text-xs text-gray-600 dark:text-graphite-muted">Zobrazit:</span>
                  <select
                    value={filters.pageSize}
                    onChange={(e) =>
                      handlePageSizeChange(Number(e.target.value))
                    }
                    className="border border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text rounded px-1 py-0.5 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-transparent"
                  >
                    <option value={10}>10</option>
                    <option value={20}>20</option>
                    <option value={50}>50</option>
                    <option value={100}>100</option>
                  </select>
                </div>
              </div>
              <div>
                <nav
                  className="relative z-0 inline-flex rounded shadow-sm dark:shadow-soft-dark -space-x-px"
                  aria-label="Pagination"
                >
                  <button
                    onClick={() => handlePageChange(filters.pageNumber - 1)}
                    disabled={filters.pageNumber <= 1}
                    className="relative inline-flex items-center px-1 py-1 rounded-l border border-gray-300 dark:border-graphite-border bg-white dark:bg-graphite-surface text-xs font-medium text-gray-500 dark:text-graphite-muted hover:bg-gray-50 dark:hover:bg-white/5 disabled:opacity-50 disabled:cursor-not-allowed"
                  >
                    <ChevronLeft className="h-3 w-3" />
                  </button>

                  {/* Page numbers */}
                  {Array.from({ length: Math.min(totalPages, 5) }, (_, i) => {
                    let pageNum: number;
                    if (totalPages <= 5) {
                      pageNum = i + 1;
                    } else if (filters.pageNumber <= 3) {
                      pageNum = i + 1;
                    } else if (filters.pageNumber >= totalPages - 2) {
                      pageNum = totalPages - 4 + i;
                    } else {
                      pageNum = filters.pageNumber - 2 + i;
                    }

                    return (
                      <button
                        key={pageNum}
                        onClick={() => handlePageChange(pageNum)}
                        className={`relative inline-flex items-center px-2 py-1 border text-xs font-medium ${
                          pageNum === filters.pageNumber
                            ? "z-10 bg-indigo-50 dark:bg-graphite-accent/10 border-indigo-500 dark:border-graphite-accent text-indigo-600 dark:text-graphite-accent"
                            : "bg-white dark:bg-graphite-surface border-gray-300 dark:border-graphite-border text-gray-500 dark:text-graphite-muted hover:bg-gray-50 dark:hover:bg-white/5"
                        }`}
                      >
                        {pageNum}
                      </button>
                    );
                  })}

                  <button
                    onClick={() => handlePageChange(filters.pageNumber + 1)}
                    disabled={filters.pageNumber >= totalPages}
                    className="relative inline-flex items-center px-1 py-1 rounded-r border border-gray-300 dark:border-graphite-border bg-white dark:bg-graphite-surface text-xs font-medium text-gray-500 dark:text-graphite-muted hover:bg-gray-50 dark:hover:bg-white/5 disabled:opacity-50 disabled:cursor-not-allowed"
                  >
                    <ChevronRight className="h-3 w-3" />
                  </button>
                </nav>
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  );
};

export { GiftPackageSortBy, type GiftPackage, type GiftPackageFilters, type GiftPackageSummary };
export default GiftPackageManufacturingList;