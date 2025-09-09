import React, { useState, useMemo } from "react";
import {
  Search,
  RefreshCw,
  Download,
  AlertTriangle,
  TrendingDown,
  CheckCircle,
  Package,
  Settings,
  ChevronLeft,
  ChevronRight,
  ChevronUp,
  ChevronDown,
  HelpCircle,
  Plus,
  Info,
} from "lucide-react";
import { useGiftPackageDetail, useAvailableGiftPackages } from "../../api/hooks/useGiftPackageManufacturing";
import { StockSeverity } from "../../api/generated/api-client";
import CatalogDetail from "./CatalogDetail";
import { PAGE_CONTAINER_HEIGHT } from "../../constants/layout";

// Filter types
interface GiftPackageFilters {
  fromDate: Date;
  toDate: Date;
  searchTerm: string;
  severity: StockSeverity | "All";
  pageNumber: number;
  pageSize: number;
  sortBy: GiftPackageSortBy;
  sortDescending: boolean;
}

enum GiftPackageSortBy {
  ProductCode = "ProductCode",
  ProductName = "ProductName",
  AvailableStock = "AvailableStock",
  DailySales = "DailySales",
  SuggestedQuantity = "SuggestedQuantity",
}

interface GiftPackage {
  code: string;
  name: string;
  availableStock: number;
  dailySales: number;
  suggestedQuantity: number;
  severity: StockSeverity;
}

interface GiftPackageSummary {
  totalPackages: number;
  criticalCount: number;
  lowStockCount: number;
  optimalCount: number;
  overstockedCount: number;
  notConfiguredCount: number;
}

const GiftPackageManufacturing: React.FC = () => {
  // State for filters
  const [filters, setFilters] = useState<GiftPackageFilters>({
    fromDate: new Date(
      new Date().getFullYear() - 1,
      new Date().getMonth(),
      new Date().getDate(),
    ),
    toDate: new Date(),
    searchTerm: "",
    severity: "All",
    pageNumber: 1,
    pageSize: 20,
    sortBy: GiftPackageSortBy.SuggestedQuantity,
    sortDescending: true,
  });

  // State for manufacturing modal 
  const [selectedPackage, setSelectedPackage] = useState<GiftPackage | null>(null);
  const [isManufactureModalOpen, setIsManufactureModalOpen] = useState(false);
  
  // State for catalog detail modal
  const [selectedProductCode, setSelectedProductCode] = useState<string | null>(null);
  const [isCatalogDetailOpen, setIsCatalogDetailOpen] = useState(false);

  // State for collapsible sections
  const [isControlsCollapsed, setIsControlsCollapsed] = useState(false);
  
  // Load gift package data from backend with date parameters
  const { data: giftPackageData, isLoading: giftPackageLoading, error: giftPackageError, refetch } = useAvailableGiftPackages({
    fromDate: filters.fromDate,
    toDate: filters.toDate
  });
  
  // Load gift package detail with components when modal is open
  const { data: giftPackageDetail, isLoading: detailLoading } = useGiftPackageDetail(
    selectedPackage?.code
  );

  // Process gift package data with date-range-based calculation
  const { giftPackages, summary } = useMemo(() => {
    if (!giftPackageData?.giftPackages) return { giftPackages: [], summary: null };
    
    const packages: GiftPackage[] = giftPackageData.giftPackages.map(pkg => {
        const availableStock = pkg.availableStock ?? 0;
        const dailySales = pkg.dailySales ?? 0;
        
        // Calculate period for reference (currently unused but may be useful for future features)
        const daysDiff = Math.ceil((filters.toDate.getTime() - filters.fromDate.getTime()) / (1000 * 60 * 60 * 24));
        
        // Calculate suggested quantity for weekly production
        const suggestedQuantity = Math.max(0, Math.ceil(dailySales * 7) - availableStock);
        
        // Severity calculation based on backend data
        let severity: StockSeverity;
        if (availableStock <= 0 || (dailySales > 0 && availableStock < dailySales * 2)) {
          severity = StockSeverity.Critical;
        } else if (dailySales > 0 && availableStock < dailySales * 7) {
          severity = StockSeverity.Low;
        } else if (pkg.overstockLimit && availableStock > pkg.overstockLimit) {
          severity = StockSeverity.Overstocked;
        } else if (dailySales > 0) {
          severity = StockSeverity.Optimal;
        } else {
          severity = StockSeverity.NotConfigured;
        }
        
        return {
          code: pkg.code!,
          name: pkg.name!,
          availableStock,
          dailySales,
          suggestedQuantity,
          severity,
        };
      });
    
    // Calculate summary
    const summaryData: GiftPackageSummary = {
      totalPackages: packages.length,
      criticalCount: packages.filter(p => p.severity === StockSeverity.Critical).length,
      lowStockCount: packages.filter(p => p.severity === StockSeverity.Low).length,
      optimalCount: packages.filter(p => p.severity === StockSeverity.Optimal).length,
      overstockedCount: packages.filter(p => p.severity === StockSeverity.Overstocked).length,
      notConfiguredCount: packages.filter(p => p.severity === StockSeverity.NotConfigured).length,
    };
    
    return { giftPackages: packages, summary: summaryData };
  }, [giftPackageData?.giftPackages, filters.fromDate, filters.toDate]);
  
  // Data automatically refetches when query key changes (filters.fromDate, filters.toDate are in queryKey)
  // No need for manual useEffect since React Query handles this automatically
  
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
        case GiftPackageSortBy.SuggestedQuantity:
          aValue = a.suggestedQuantity;
          bValue = b.suggestedQuantity;
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
  const handleExport = () => {
    console.log("Export to CSV");
  };

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

    handleFilterChange({ fromDate, toDate });
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

  // Handle severity filter click from summary cards
  const handleSeverityFilterClick = (severity: StockSeverity | "All") => {
    handleFilterChange({ severity });
  };

  // Manufacturing modal handlers
  const handleRowClick = (pkg: GiftPackage) => {
    setSelectedPackage(pkg);
    setIsManufactureModalOpen(true);
  };
  
  // Catalog detail handlers
  const handleCatalogDetailClick = (productCode: string) => {
    setSelectedProductCode(productCode);
    setIsCatalogDetailOpen(true);
  };
  
  const handleCloseCatalogDetail = () => {
    setIsCatalogDetailOpen(false);
    setSelectedProductCode(null);
  };
  
  const handleCloseManufactureModal = () => {
    setIsManufactureModalOpen(false);
    setSelectedPackage(null);
  };
  
  const handleManufacture = async (quantity: number) => {
    if (!selectedPackage) return;
    
    try {
      console.log(`Výroba ${quantity}x ${selectedPackage.name}`);
      // TODO: Call actual manufacturing API
      handleCloseManufactureModal();
      refetch(); // Refresh data after manufacturing
    } catch (error) {
      console.error('Manufacturing error:', error);
    }
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
        className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider cursor-pointer hover:bg-gray-100 select-none ${className}`}
        onClick={() => handleSort(column)}
      >
        <div className="flex items-center space-x-1">
          <span>{children}</span>
          <div className="flex flex-col">
            <ChevronUp
              className={`h-3 w-3 ${isAscending ? "text-indigo-600" : "text-gray-300"}`}
            />
            <ChevronDown
              className={`h-3 w-3 -mt-1 ${isDescending ? "text-indigo-600" : "text-gray-300"}`}
            />
          </div>
        </div>
      </th>
    );
  };

  // Get row background color based on severity
  const getRowColorClass = (severity: StockSeverity) => {
    switch (severity) {
      case StockSeverity.Critical:
        return "bg-red-50/30 hover:bg-red-50/50";
      case StockSeverity.Low:
        return "bg-amber-50/30 hover:bg-amber-50/50";
      case StockSeverity.Optimal:
        return "bg-emerald-50/30 hover:bg-emerald-50/50";
      case StockSeverity.Overstocked:
        return "bg-blue-50/30 hover:bg-blue-50/50";
      case StockSeverity.NotConfigured:
        return "bg-gray-50/30 hover:bg-gray-50/50";
      default:
        return "hover:bg-gray-50";
    }
  };
  
  // Get color strip for severity
  const getSeverityStripColor = (severity: StockSeverity) => {
    if (filters.severity !== "All") {
      return "";
    }

    switch (severity) {
      case StockSeverity.Critical:
        return "bg-red-500";
      case StockSeverity.Low:
        return "bg-amber-500";
      case StockSeverity.Optimal:
        return "bg-emerald-500";
      case StockSeverity.Overstocked:
        return "bg-blue-500";
      case StockSeverity.NotConfigured:
        return "bg-gray-400";
      default:
        return "";
    }
  };
  
  const isLoading = giftPackageLoading;
  const error = giftPackageError;
  
  if (error) {
    return (
      <div className="min-h-screen bg-gray-50 px-4 py-8">
        <div className="max-w-7xl mx-auto">
          <div className="bg-red-50 border border-red-200 rounded-lg p-6">
            <div className="flex items-center">
              <AlertTriangle className="h-5 w-5 text-red-400 mr-2" />
              <h3 className="text-lg font-medium text-red-800">
                Chyba při načítání dat
              </h3>
            </div>
            <p className="mt-2 text-sm text-red-700">
              {error instanceof Error ? error.message : "Neočekávaná chyba"}
            </p>
            <button
              onClick={() => refetch()}
              className="mt-4 bg-red-100 hover:bg-red-200 text-red-800 px-4 py-2 rounded-md text-sm font-medium"
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
        <h1 className="text-lg font-semibold text-gray-900">
          Výroba dárkových balíčků
        </h1>
      </div>

      {/* Controls - Single Collapsible Block */}
      <div className="flex-shrink-0 bg-white rounded-lg shadow mb-4">
        <div className="p-3 border-b border-gray-200">
          <div className="flex items-center justify-between">
            <button
              onClick={() => setIsControlsCollapsed(!isControlsCollapsed)}
              className="flex items-center space-x-2 text-sm font-medium text-gray-900 hover:text-gray-700"
            >
              {isControlsCollapsed ? (
                <ChevronRight className="h-4 w-4" />
              ) : (
                <ChevronDown className="h-4 w-4" />
              )}
              <span>Filtry a nastavení</span>
              {summary && (
                <span className="text-xs text-gray-500">
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
                    <div className="flex items-center space-x-2 text-xs">
                      <button
                        onClick={() => handleSeverityFilterClick("All")}
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
                        onClick={() => handleSeverityFilterClick(StockSeverity.Critical)}
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
                        onClick={() => handleSeverityFilterClick(StockSeverity.Low)}
                        className={`px-1 py-0.5 rounded transition-colors hover:bg-amber-50 ${
                          filters.severity === StockSeverity.Low
                            ? "bg-amber-50 ring-1 ring-amber-300"
                            : ""
                        }`}
                        title="Nízké zásoby"
                      >
                        <span className="text-orange-600 font-medium">
                          {summary.lowStockCount}
                        </span>
                      </button>
                      <button
                        onClick={() => handleSeverityFilterClick(StockSeverity.Optimal)}
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
                  )}
                  {/* Search field when collapsed */}
                  <div className="flex-1 max-w-xs">
                    <div className="relative">
                      <Search className="absolute left-2 top-1/2 transform -translate-y-1/2 h-3 w-3 text-gray-400" />
                      <input
                        type="text"
                        value={filters.searchTerm || ""}
                        onChange={(e) =>
                          handleFilterChange({ searchTerm: e.target.value })
                        }
                        placeholder="Vyhledat..."
                        className="pl-7 w-full border border-gray-300 rounded-md px-2 py-1 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-transparent"
                      />
                    </div>
                  </div>
                </>
              )}

              {/* Action buttons - always visible */}
              <button
                onClick={() => refetch()}
                disabled={giftPackageLoading}
                className="flex items-center px-2 py-1 border border-gray-300 rounded-md shadow-sm text-xs font-medium text-gray-700 bg-white hover:bg-gray-50 disabled:opacity-50"
              >
                <RefreshCw
                  className={`h-3 w-3 mr-1 ${giftPackageLoading ? "animate-spin" : ""}`}
                />
                {isControlsCollapsed ? "" : "Obnovit"}
              </button>
              <button
                onClick={handleExport}
                className="flex items-center px-2 py-1 border border-gray-300 rounded-md shadow-sm text-xs font-medium text-gray-700 bg-white hover:bg-gray-50"
              >
                <Download className="h-3 w-3 mr-1" />
                {isControlsCollapsed ? "" : "Export"}
              </button>

              {/* Help */}
              <div className="relative group">
                <HelpCircle className="h-4 w-4 text-gray-400 cursor-help" />
                <div className="absolute right-0 top-6 w-80 bg-gray-900 text-white text-xs rounded-lg p-3 opacity-0 group-hover:opacity-100 transition-opacity z-10 pointer-events-none">
                  <div className="space-y-2">
                    <div className="flex items-start">
                      <div className="w-3 h-3 bg-red-200 rounded-sm mr-2 mt-0.5 flex-shrink-0"></div>
                      <div>
                        <span className="font-medium text-red-200">
                          Kritické:
                        </span>{" "}
                        Nulové zásoby nebo vysoká poptávka
                      </div>
                    </div>
                    <div className="flex items-start">
                      <div className="w-3 h-3 bg-amber-200 rounded-sm mr-2 mt-0.5 flex-shrink-0"></div>
                      <div>
                        <span className="font-medium text-amber-200">
                          Nízké:
                        </span>{" "}
                        Doporučuje se výroba
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
                    <div className="flex items-start">
                      <div className="w-3 h-3 bg-blue-200 rounded-sm mr-2 mt-0.5 flex-shrink-0"></div>
                      <div>
                        <span className="font-medium text-blue-200">
                          Přeskladněno:
                        </span>{" "}
                        Vysoké zásoby
                      </div>
                    </div>
                    <div className="flex items-start">
                      <div className="w-3 h-3 bg-gray-200 rounded-sm mr-2 mt-0.5 flex-shrink-0"></div>
                      <div>
                        <span className="font-medium text-gray-200">
                          Nezkonfigurováno:
                        </span>{" "}
                        Chybí data o prodeji
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
              <div>
                <h3 className="text-xs font-medium text-gray-700 mb-2">
                  Přehled stavů zásob
                </h3>
                <div className="flex flex-wrap items-center gap-2 text-xs">
                  <button
                    onClick={() => handleSeverityFilterClick("All")}
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
                    onClick={() => handleSeverityFilterClick(StockSeverity.Critical)}
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
                    onClick={() => handleSeverityFilterClick(StockSeverity.Low)}
                    className={`flex items-center px-2 py-1 rounded-md transition-colors hover:bg-amber-50 ${
                      filters.severity === StockSeverity.Low
                        ? "bg-amber-50 ring-1 ring-amber-300"
                        : ""
                    }`}
                  >
                    <TrendingDown className="h-3 w-3 text-orange-500 mr-1" />
                    <span className="text-gray-600">Nízké:</span>
                    <span className="font-semibold text-orange-600 ml-1">
                      {summary.lowStockCount}
                    </span>
                  </button>

                  <button
                    onClick={() => handleSeverityFilterClick(StockSeverity.Optimal)}
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
                    onClick={() => handleSeverityFilterClick(StockSeverity.Overstocked)}
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
                    onClick={() => handleSeverityFilterClick(StockSeverity.NotConfigured)}
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
            )}

            {/* Filters */}
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
                        handleFilterChange({ searchTerm: e.target.value })
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
                      handleFilterChange({ fromDate: new Date(e.target.value) })
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
                      handleFilterChange({ toDate: new Date(e.target.value) })
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
          </div>
        )}
      </div>

      {/* Results Table */}
      <div className="flex-1 bg-white rounded-lg shadow overflow-hidden flex flex-col min-h-0">
        {isLoading ? (
          <div className="flex items-center justify-center py-12">
            <RefreshCw className="h-8 w-8 animate-spin text-gray-400" />
            <span className="ml-2 text-gray-600">Načítání balíčků...</span>
          </div>
        ) : filteredPackages.length === 0 ? (
          <div className="flex items-center justify-center py-12">
            <div className="text-center">
              <Package className="h-12 w-12 text-gray-400 mx-auto mb-4" />
              <h3 className="text-lg font-medium text-gray-900 mb-2">
                Žádné výsledky
              </h3>
              <p className="text-gray-600">
                Zkuste upravit filtry nebo vyhledávací kritéria.
              </p>
            </div>
          </div>
        ) : (
          <div className="flex-1 overflow-auto">
            <table className="min-w-full divide-y divide-gray-200">
              <thead className="bg-gray-50 sticky top-0 z-10">
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
                    column={GiftPackageSortBy.DailySales}
                    className="text-right hidden md:table-cell"
                  >
                    Prodeje/den
                  </SortableHeader>
                  <SortableHeader
                    column={GiftPackageSortBy.SuggestedQuantity}
                    className="text-right"
                  >
                    Doporučeno
                  </SortableHeader>
                </tr>
              </thead>
              <tbody className="bg-white divide-y divide-gray-200">
                {filteredPackages.map((pkg) => (
                  <tr
                    key={pkg.code}
                    className={`${getRowColorClass(pkg.severity)} hover:bg-gray-50 cursor-pointer transition-colors duration-150`}
                    onClick={() => handleRowClick(pkg)}
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
                            <div className="text-sm text-gray-900 truncate">
                              {pkg.name}
                            </div>
                            <button
                              onClick={(e) => {
                                e.stopPropagation();
                                handleCatalogDetailClick(pkg.code);
                              }}
                              className="flex-shrink-0 text-gray-400 hover:text-indigo-600 transition-colors"
                              title="Zobrazit detail produktu"
                            >
                              <Info className="h-4 w-4" />
                            </button>
                          </div>
                          <div className="text-xs text-gray-500">
                            {pkg.code}
                          </div>
                        </div>
                      </div>
                    </td>

                    {/* Available Stock */}
                    <td className="px-6 py-4 whitespace-nowrap text-right text-xs text-gray-900">
                      <div className="font-bold">
                        {pkg.availableStock.toFixed(0)}
                      </div>
                    </td>

                    {/* Daily Sales - Hidden on mobile */}
                    <td className="px-6 py-4 whitespace-nowrap text-right text-xs text-gray-900 hidden md:table-cell">
                      <div>{pkg.dailySales.toFixed(1)}</div>
                      <div className="text-xs text-gray-500 md:hidden">
                        {pkg.dailySales.toFixed(1)}/den
                      </div>
                    </td>

                    {/* Suggested Quantity */}
                    <td className="px-6 py-4 whitespace-nowrap text-right text-xs text-gray-900">
                      <div className={`font-bold ${
                        pkg.suggestedQuantity > 0 ? "text-orange-600" : "text-green-600"
                      }`}>
                        {pkg.suggestedQuantity}
                      </div>
                      <div className="text-xs text-gray-500 md:hidden">
                        {pkg.dailySales.toFixed(1)}/den
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
          <div className="flex-shrink-0 bg-white px-3 py-2 flex items-center justify-between border-t border-gray-200 text-xs">
            <div className="flex-1 flex justify-between sm:hidden">
              <button
                onClick={() => handlePageChange(filters.pageNumber - 1)}
                disabled={filters.pageNumber <= 1}
                className="relative inline-flex items-center px-2 py-1 border border-gray-300 text-xs font-medium rounded text-gray-700 bg-white hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
              >
                Předchozí
              </button>
              <button
                onClick={() => handlePageChange(filters.pageNumber + 1)}
                disabled={filters.pageNumber >= totalPages}
                className="ml-2 relative inline-flex items-center px-2 py-1 border border-gray-300 text-xs font-medium rounded text-gray-700 bg-white hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
              >
                Další
              </button>
            </div>
            <div className="hidden sm:flex-1 sm:flex sm:items-center sm:justify-between">
              <div className="flex items-center space-x-3">
                <p className="text-xs text-gray-600">
                  {(filters.pageNumber - 1) * filters.pageSize + 1}-
                  {Math.min(
                    filters.pageNumber * filters.pageSize,
                    totalCount,
                  )}{" "}
                  z {totalCount}
                </p>
                <div className="flex items-center space-x-1">
                  <span className="text-xs text-gray-600">Zobrazit:</span>
                  <select
                    value={filters.pageSize}
                    onChange={(e) =>
                      handlePageSizeChange(Number(e.target.value))
                    }
                    className="border border-gray-300 rounded px-1 py-0.5 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-transparent"
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
                  className="relative z-0 inline-flex rounded shadow-sm -space-x-px"
                  aria-label="Pagination"
                >
                  <button
                    onClick={() => handlePageChange(filters.pageNumber - 1)}
                    disabled={filters.pageNumber <= 1}
                    className="relative inline-flex items-center px-1 py-1 rounded-l border border-gray-300 bg-white text-xs font-medium text-gray-500 hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
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
                            ? "z-10 bg-indigo-50 border-indigo-500 text-indigo-600"
                            : "bg-white border-gray-300 text-gray-500 hover:bg-gray-50"
                        }`}
                      >
                        {pageNum}
                      </button>
                    );
                  })}

                  <button
                    onClick={() => handlePageChange(filters.pageNumber + 1)}
                    disabled={filters.pageNumber >= totalPages}
                    className="relative inline-flex items-center px-1 py-1 rounded-r border border-gray-300 bg-white text-xs font-medium text-gray-500 hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
                  >
                    <ChevronRight className="h-3 w-3" />
                  </button>
                </nav>
              </div>
            </div>
          </div>
        )}
      </div>

      {/* Manufacturing Modal with Gift Package Components */}
      {isManufactureModalOpen && selectedPackage && (
        <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50 p-4">
          <div className="bg-white rounded-lg shadow-xl w-full max-w-5xl max-h-[90vh] flex flex-col">
            {/* Header */}
            <div className="flex items-center justify-between p-6 border-b border-gray-200">
              <div>
                <h2 className="text-xl font-semibold text-gray-900">
                  Balíček: {selectedPackage.name}
                </h2>
                <p className="text-sm text-gray-600 mt-1">
                  Kód: {selectedPackage.code}
                </p>
              </div>
              <button
                onClick={handleCloseManufactureModal}
                className="text-gray-400 hover:text-gray-600 transition-colors"
              >
                <Plus className="h-6 w-6 rotate-45" />
              </button>
            </div>

            {/* Content - scrollable */}
            <div className="flex-1 overflow-auto p-6 space-y-6">
              {/* Package Info */}
              <div className="bg-gray-50 rounded-lg p-4">
                <h3 className="text-lg font-medium text-gray-900 mb-2">
                  Informace o balíčku
                </h3>
                <div className="grid grid-cols-2 md:grid-cols-4 gap-4 text-sm">
                  <div>
                    <span className="text-gray-600">Aktuální sklad:</span>
                    <div className="font-semibold text-gray-900">{selectedPackage.availableStock.toFixed(0)} ks</div>
                  </div>
                  <div>
                    <span className="text-gray-600">Prodeje/den:</span>
                    <div className="font-semibold text-gray-900">{selectedPackage.dailySales.toFixed(1)} ks</div>
                  </div>
                  <div>
                    <span className="text-gray-600">Doporučeno:</span>
                    <div className="font-semibold text-orange-600">{selectedPackage.suggestedQuantity} ks</div>
                  </div>
                  <div>
                    <span className="text-gray-600">Týdenní spotřeba:</span>
                    <div className="font-semibold text-gray-900">{(selectedPackage.dailySales * 7).toFixed(1)} ks</div>
                  </div>
                </div>
              </div>

              {/* Gift Package Components */}
              <div className="bg-white border rounded-lg">
                <div className="px-4 py-3 bg-gray-50 border-b rounded-t-lg">
                  <h3 className="text-lg font-medium text-gray-900 flex items-center">
                    <Package className="h-5 w-5 mr-2" />
                    Komponenty balíčku
                    {giftPackageDetail?.giftPackage?.ingredients && (
                      <span className="ml-2 text-sm font-normal text-gray-600">
                        ({giftPackageDetail.giftPackage.ingredients.length})
                      </span>
                    )}
                  </h3>
                </div>
                
                <div className="max-h-80 overflow-y-auto">
                  {detailLoading ? (
                    <div className="flex items-center justify-center py-8">
                      <RefreshCw className="h-6 w-6 animate-spin text-gray-400 mr-2" />
                      <span className="text-gray-600">Načítání komponent...</span>
                    </div>
                  ) : giftPackageDetail?.giftPackage?.ingredients && giftPackageDetail.giftPackage.ingredients.length > 0 ? (
                    <table className="min-w-full divide-y divide-gray-200">
                      <thead className="bg-gray-50 sticky top-0">
                        <tr>
                          <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                            Komponenta
                          </th>
                          <th className="px-4 py-2 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                            Potřeba/ks
                          </th>
                          <th className="px-4 py-2 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                            Skladem
                          </th>
                          <th className="px-4 py-2 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                            Stav
                          </th>
                        </tr>
                      </thead>
                      <tbody className="bg-white divide-y divide-gray-200">
                        {giftPackageDetail.giftPackage.ingredients.map((ingredient, index) => {
                          const requiredQuantity = ingredient.requiredQuantity || 0;
                          const availableStock = ingredient.availableStock || 0;
                          const isInStock = ingredient.hasSufficientStock || false;
                          
                          return (
                            <tr key={index} className="hover:bg-gray-50">
                              <td className="px-4 py-3">
                                <div>
                                  <div className="text-sm font-medium text-gray-900">
                                    {ingredient.productName}
                                  </div>
                                  <div className="text-sm text-gray-500">
                                    {ingredient.productCode}
                                  </div>
                                </div>
                              </td>
                              <td className="px-4 py-3 text-sm text-right text-gray-900">
                                {requiredQuantity.toFixed(1)}
                              </td>
                              <td className="px-4 py-3 text-sm text-right text-gray-900">
                                {availableStock.toFixed(1)}
                              </td>
                              <td className="px-4 py-3 text-right">
                                <span
                                  className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${
                                    isInStock
                                      ? "bg-green-100 text-green-800"
                                      : "bg-red-100 text-red-800"
                                  }`}
                                >
                                  {isInStock ? "✓ Skladem" : "⚠ Chybí"}
                                </span>
                              </td>
                            </tr>
                          );
                        })}
                      </tbody>
                    </table>
                  ) : (
                    <div className="flex items-center justify-center py-8 text-gray-500">
                      <Package className="h-8 w-8 mr-3" />
                      <span>Komponenty nejsou k dispozici</span>
                    </div>
                  )}
                </div>
              </div>

              {/* Manufacturing Form */}
              <div className="bg-indigo-50 rounded-lg p-6">
                <h3 className="text-lg font-medium text-gray-900 mb-4">
                  Výrobní příkaz
                </h3>
                <div className="flex items-center space-x-4">
                  <div className="flex-1">
                    <label className="block text-sm font-medium text-gray-700 mb-2">
                      Množství k výrobě
                    </label>
                    <input
                      type="number"
                      min="1"
                      defaultValue={Math.max(1, selectedPackage.suggestedQuantity)}
                      className="w-full border border-gray-300 rounded-lg px-4 py-2 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent"
                      placeholder="Zadejte množství"
                    />
                  </div>
                  <div className="flex-shrink-0 pt-7">
                    <button
                      onClick={() => {
                        // TODO: Get quantity from input
                        handleManufacture(selectedPackage.suggestedQuantity);
                      }}
                      className="flex items-center px-6 py-2 border border-transparent text-sm font-medium rounded-lg text-white bg-indigo-600 hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500"
                    >
                      <Plus className="h-4 w-4 mr-2" />
                      Zahájit výrobu
                    </button>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>
      )}
      
      {/* Catalog Detail Modal */}
      <CatalogDetail
        productCode={selectedProductCode}
        isOpen={isCatalogDetailOpen}
        onClose={handleCloseCatalogDetail}
        defaultTab="basic"
      />
    </div>
  );
};

export default GiftPackageManufacturing;