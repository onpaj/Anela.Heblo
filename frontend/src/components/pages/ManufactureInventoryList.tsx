import React, { useState } from "react";
import { useSearchParams } from "react-router-dom";
import {
  Search,
  Filter,
  AlertCircle,
  Loader2,
  ChevronUp,
  ChevronDown,
  ChevronLeft,
  ChevronRight,
  Wrench,
} from "lucide-react";
import {
  ProductType,
  CatalogItemDto,
} from "../../api/hooks/useCatalog";
import { useManufactureInventoryQuery } from "../../api/hooks/useManufactureInventory";
import CatalogDetail from "./CatalogDetail";
import ManufactureInventoryModal from "../inventory/ManufactureInventoryDetail";
import { PAGE_CONTAINER_HEIGHT } from "../../constants/layout";
import { useScreenView } from '../../telemetry/useScreenView';

// Filter for manufacture inventory - only show materials
const allowedManufactureInventoryTypes: ProductType[] = [
  ProductType.Material,
];

const productTypeLabels: Record<ProductType, string> = {
  [ProductType.Product]: "Produkt",
  [ProductType.Goods]: "Zboží",
  [ProductType.Material]: "Materiál",
  [ProductType.SemiProduct]: "Polotovar",
  [ProductType.Set]: "Dárkový balíček",
  [ProductType.UNDEFINED]: "Nedefinováno",
};

// Expiration bucket boundaries - keep in sync with the "Expirace surovin" dashboard tile
// (MaterialExpirationSummaryTile: SoonDays = 30, HorizonDays = 90).
const EXPIRATION_SOON_DAYS = 30;
const EXPIRATION_HORIZON_DAYS = 90;

const startOfToday = (): Date => {
  const now = new Date();
  return new Date(now.getFullYear(), now.getMonth(), now.getDate());
};

const addDays = (date: Date, days: number): Date => {
  const result = new Date(date);
  result.setDate(result.getDate() + days);
  return result;
};

// Format a Date to a local "yyyy-MM-dd" string (matches <input type="date"> values)
const toIsoDate = (date: Date): string => {
  const pad = (n: number) => (n < 10 ? `0${n}` : `${n}`);
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
};

const ManufactureInventoryList: React.FC = () => {
  useScreenView('Manufacturing', 'ManufactureInventory');

  // Filter states - separate input values from applied filters
  const [productNameInput, setProductNameInput] = useState("");
  const [productCodeInput, setProductCodeInput] = useState("");
  const [productNameFilter, setProductNameFilter] = useState("");
  const [productCodeFilter, setProductCodeFilter] = useState("");
  // Start with Material as the default type for manufacture inventory
  const [productTypeFilter, setProductTypeFilter] = useState<ProductType | "">(
    ProductType.Material,
  );

  // Expiration range filter - ISO "yyyy-MM-dd" strings (as produced by <input type="date">)
  const [expirationFromInput, setExpirationFromInput] = useState("");
  const [expirationToInput, setExpirationToInput] = useState("");
  const [expirationFromFilter, setExpirationFromFilter] = useState("");
  const [expirationToFilter, setExpirationToFilter] = useState("");

  // Pagination states
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(20);

  // Sorting states - default sort by lastInventoryDays descending
  const [sortBy, setSortBy] = useState<string>("lastInventoryDays");
  const [sortDescending, setSortDescending] = useState(true);

  // Read initial sort from URL (e.g. when navigating from the material expiration dashboard tile)
  const [searchParams] = useSearchParams();

  // Modal states
  const [selectedItem, setSelectedItem] = useState<CatalogItemDto | null>(null);
  const [isDetailModalOpen, setIsDetailModalOpen] = useState(false);
  const [selectedInventoryItem, setSelectedInventoryItem] = useState<CatalogItemDto | null>(null);
  const [isInventoryModalOpen, setIsInventoryModalOpen] = useState(false);

  // Use custom manufacture inventory hook that handles materials filtering
  const {
    data,
    isLoading: loading,
    error,
    refetch,
  } = useManufactureInventoryQuery(
    productNameFilter,
    productCodeFilter,
    productTypeFilter,
    pageNumber,
    pageSize,
    sortBy,
    sortDescending,
    expirationFromFilter,
    expirationToFilter,
  );

  // Items are already filtered by the manufacture inventory hook
  const filteredItems = data?.items || [];
  
  const totalCount = data?.totalCount || 0;
  const totalPages = Math.ceil(totalCount / pageSize);

  // Handler for applying filters on Enter
  const handleApplyFilters = async () => {
    setProductNameFilter(productNameInput);
    setProductCodeFilter(productCodeInput);
    setExpirationFromFilter(expirationFromInput);
    setExpirationToFilter(expirationToInput);
    setPageNumber(1);

    await refetch();
  };

  // Handler for Enter key press
  const handleKeyDown = (event: React.KeyboardEvent) => {
    if (event.key === "Enter") {
      handleApplyFilters();
    }
  };

  // Handler for clearing all filters
  const handleClearFilters = async () => {
    setProductNameInput("");
    setProductCodeInput("");
    setProductNameFilter("");
    setProductCodeFilter("");
    setProductTypeFilter(ProductType.Material);
    setExpirationFromInput("");
    setExpirationToInput("");
    setExpirationFromFilter("");
    setExpirationToFilter("");
    setPageNumber(1);

    await refetch();
  };

  // Prefill and apply an expiration range preset (dates as ISO "yyyy-MM-dd")
  const applyExpirationPreset = (from: string, to: string) => {
    setExpirationFromInput(from);
    setExpirationToInput(to);
    setExpirationFromFilter(from);
    setExpirationToFilter(to);
    setPageNumber(1);
  };

  // Expiration bucket presets - mirror the "Expirace surovin" dashboard tile boundaries
  const handleExpiredPreset = () => {
    // Everything up to and including yesterday = expired (expiration < today)
    applyExpirationPreset("", toIsoDate(addDays(startOfToday(), -1)));
  };

  const handleWithin30Preset = () => {
    const today = startOfToday();
    applyExpirationPreset(toIsoDate(today), toIsoDate(addDays(today, EXPIRATION_SOON_DAYS)));
  };

  const handleWithin90Preset = () => {
    const today = startOfToday();
    applyExpirationPreset(
      toIsoDate(addDays(today, EXPIRATION_SOON_DAYS + 1)),
      toIsoDate(addDays(today, EXPIRATION_HORIZON_DAYS)),
    );
  };

  // Sorting handler
  const handleSort = (column: string) => {
    if (sortBy === column) {
      setSortDescending(!sortDescending);
    } else {
      setSortBy(column);
      setSortDescending(false);
    }
  };

  // Pagination handlers
  const handlePageChange = (newPage: number) => {
    if (newPage >= 1 && newPage <= totalPages) {
      setPageNumber(newPage);
    }
  };

  const handlePageSizeChange = (newPageSize: number) => {
    setPageSize(newPageSize);
    setPageNumber(1);
  };

  // Reset pagination when type filter changes
  React.useEffect(() => {
    setPageNumber(1);
  }, [productTypeFilter]);

  // Seed sort state from URL query params (e.g. from the expiration dashboard tile)
  React.useEffect(() => {
    const sortByParam = searchParams.get("sortBy");
    const sortDescParam = searchParams.get("sortDescending");
    if (sortByParam) {
      setSortBy(sortByParam);
    }
    if (sortDescParam !== null) {
      setSortDescending(sortDescParam === "true");
    }
  }, [searchParams]);

  // Modal handlers
  const handleItemClick = (event: React.MouseEvent, item: CatalogItemDto) => {
    event.stopPropagation(); // Prevent row click from triggering
    setSelectedItem(item);
    setIsDetailModalOpen(true);
  };

  const handleCloseDetail = () => {
    setIsDetailModalOpen(false);
    setSelectedItem(null);
  };

  // Inventory modal handlers
  const handleInventoryClick = (item: CatalogItemDto) => {
    setSelectedInventoryItem(item);
    setIsInventoryModalOpen(true);
  };

  const handleCloseInventory = () => {
    setIsInventoryModalOpen(false);
    setSelectedInventoryItem(null);
  };


  // Sortable header component
  const SortableHeader: React.FC<{
    column: string;
    children: React.ReactNode;
    align?: 'left' | 'center';
  }> = ({ column, children, align = 'left' }) => {
    const isActive = sortBy === column;
    const isAscending = isActive && !sortDescending;
    const isDescending = isActive && sortDescending;

    const alignmentClass = align === 'center' ? 'text-center justify-center' : 'text-left justify-start';

    return (
      <th
        scope="col"
        className={`px-6 py-4 ${alignmentClass} text-sm font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider cursor-pointer hover:bg-gray-100 dark:hover:bg-white/5 select-none`}
        onClick={() => handleSort(column)}
      >
        <div className={`flex items-center space-x-1 ${align === 'center' ? 'justify-center' : 'justify-start'}`}>
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


  if (loading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="flex items-center space-x-2">
          <Loader2 className="h-5 w-5 animate-spin text-indigo-500" />
          <div className="text-gray-500 dark:text-graphite-muted">Načítání zásob materiálů...</div>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="flex items-center space-x-2 text-red-600 dark:text-red-400">
          <AlertCircle className="h-5 w-5" />
          <div>Chyba při načítání zásob materiálů: {error.message}</div>
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
        <div className="flex items-center space-x-2">
          <Wrench className="h-6 w-6 text-indigo-600 dark:text-graphite-accent" />
          <h1 className="text-lg font-semibold text-gray-900 dark:text-graphite-text">Zásoby materiálů</h1>
        </div>
      </div>

      {/* Filters - Fixed */}
      <div className="flex-shrink-0 bg-white dark:bg-graphite-surface shadow dark:shadow-soft-dark rounded-lg p-4 mb-4">
        <div className="flex items-center justify-between flex-wrap gap-3">
          <div className="flex items-center gap-3 flex-1 min-w-0">
            <div className="flex items-center">
              <Filter className="h-4 w-4 text-gray-400 dark:text-graphite-faint mr-2" />
              <span className="text-sm font-medium text-gray-900 dark:text-graphite-text">Filtry:</span>
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
                  placeholder="Název materiálu..."
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
                  placeholder="Kód materiálu..."
                />
              </div>
            </div>

            <div className="flex-1 max-w-xs">
              <select
                id="productType"
                value={productTypeFilter}
                onChange={(e) =>
                  setProductTypeFilter(
                    e.target.value === ""
                      ? ""
                      : (e.target.value as ProductType),
                  )
                }
                className="block w-full pl-3 pr-10 py-2 text-base border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm rounded-md"
              >
                <option value="">Všechny typy</option>
                {allowedManufactureInventoryTypes.map((productType) => (
                  <option key={productType} value={productType}>
                    {productTypeLabels[productType]}
                  </option>
                ))}
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

        {/* Expiration range filter */}
        <div className="flex items-center flex-wrap gap-3 mt-3 pt-3 border-t border-gray-100 dark:border-graphite-border">
          <span className="text-sm font-medium text-gray-900 dark:text-graphite-text">Expirace:</span>

          <div className="flex items-center gap-2">
            <label htmlFor="expirationFrom" className="text-sm text-gray-600 dark:text-graphite-muted whitespace-nowrap">
              Od
            </label>
            <input
              type="date"
              id="expirationFrom"
              value={expirationFromInput}
              onChange={(e) => setExpirationFromInput(e.target.value)}
              onKeyDown={handleKeyDown}
              className="py-2 px-3 sm:text-sm border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text rounded-md focus:outline-none focus:ring-indigo-500 focus:border-indigo-500"
            />
          </div>

          <div className="flex items-center gap-2">
            <label htmlFor="expirationTo" className="text-sm text-gray-600 dark:text-graphite-muted whitespace-nowrap">
              Do
            </label>
            <input
              type="date"
              id="expirationTo"
              value={expirationToInput}
              onChange={(e) => setExpirationToInput(e.target.value)}
              onKeyDown={handleKeyDown}
              className="py-2 px-3 sm:text-sm border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text rounded-md focus:outline-none focus:ring-indigo-500 focus:border-indigo-500"
            />
          </div>

          <div className="flex items-center gap-2">
            <button
              onClick={handleExpiredPreset}
              className="px-3 py-2 text-sm font-medium rounded-md border border-red-300 text-red-700 hover:bg-red-50 dark:border-red-500/40 dark:text-red-300 dark:hover:bg-red-500/10 transition-colors"
            >
              Po expiraci
            </button>
            <button
              onClick={handleWithin30Preset}
              className="px-3 py-2 text-sm font-medium rounded-md border border-orange-300 text-orange-700 hover:bg-orange-50 dark:border-orange-500/40 dark:text-orange-300 dark:hover:bg-orange-500/10 transition-colors"
            >
              ≤ 30 dní
            </button>
            <button
              onClick={handleWithin90Preset}
              className="px-3 py-2 text-sm font-medium rounded-md border border-amber-300 text-amber-700 hover:bg-amber-50 dark:border-amber-500/40 dark:text-amber-300 dark:hover:bg-amber-500/10 transition-colors"
            >
              31–90 dní
            </button>
          </div>
        </div>
      </div>

      {/* Data Grid - Scrollable */}
      <div className="flex-1 bg-white dark:bg-graphite-surface shadow dark:shadow-soft-dark rounded-lg overflow-hidden flex flex-col min-h-0">
        <div className="flex-1 overflow-auto">
          <table className="min-w-full divide-y divide-gray-200 dark:divide-graphite-border">
            <thead className="bg-gray-50 dark:bg-graphite-surface-2 sticky top-0 z-10">
              <tr>
                <SortableHeader column="productCode">
                  Kód materiálu
                </SortableHeader>
                <SortableHeader column="productName">
                  Název materiálu
                </SortableHeader>
                <SortableHeader column="lastInventoryDays" align="center">Posl. Inventura</SortableHeader>
                <SortableHeader column="expiration" align="center">Expirace</SortableHeader>
                <SortableHeader column="available" align="center">Skladem</SortableHeader>

              </tr>
            </thead>
            <tbody className="bg-white dark:bg-graphite-surface divide-y divide-gray-200 dark:divide-graphite-border">
              {filteredItems.map((item) => {
                const available = Math.round((item.stock?.available || 0) * 100) / 100;

                return (
                  <tr
                    key={item.productCode}
                    className="hover:bg-gray-50 dark:hover:bg-white/5 cursor-pointer transition-colors duration-150"
                    onClick={() => handleInventoryClick(item)}
                    title="Klikněte pro zobrazení detailu"
                  >
                    <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900 dark:text-graphite-text">
                      <span
                        onClick={(e) => handleItemClick(e, item)}
                        title="Klikněte pro inventarizaci materiálu"
                      >
                        {item.productCode}
                      </span>
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900 dark:text-graphite-text">
                      {item.productName}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-center">
                      {item.lastStockTaking ? (
                        <div
                          className="text-sm text-gray-700 dark:text-graphite-muted cursor-help inline-block"
                          title={`Poslední inventura: ${new Date(item.lastStockTaking).toLocaleString('cs-CZ', { 
                            day: '2-digit', 
                            month: '2-digit', 
                            year: 'numeric', 
                            hour: '2-digit', 
                            minute: '2-digit', 
                            second: '2-digit' 
                          })}`}
                        >
                          {Math.floor((new Date().getTime() - new Date(item.lastStockTaking).getTime()) / (1000 * 60 * 60 * 24))} d
                        </div>
                      ) : (
                        <span className="text-gray-400 dark:text-graphite-faint text-sm">-</span>
                      )}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-center">
                      {item.minimalExpiration ? (
                        <span className="text-sm text-gray-700 dark:text-graphite-muted">
                          {new Date(item.minimalExpiration).toLocaleDateString('cs-CZ', {
                            day: '2-digit',
                            month: '2-digit',
                            year: 'numeric',
                          })}
                        </span>
                      ) : (
                        <span className="text-gray-400 dark:text-graphite-faint text-sm">-</span>
                      )}
                    </td>
                    <td className="px-6 py-5 whitespace-nowrap text-center">
                      <span
                        className="inline-flex items-center px-4 py-2 rounded-full text-base font-semibold bg-green-100 dark:bg-emerald-900/30 text-green-800 dark:text-emerald-300 justify-center inventory-badge hover:bg-green-200 dark:hover:bg-emerald-900/50 hover:text-green-900 dark:hover:text-emerald-200 cursor-pointer"
                      >
                        {available}
                      </span>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>

          {filteredItems.length === 0 && (
            <div className="text-center py-8">
              <Wrench className="h-12 w-12 text-gray-400 dark:text-graphite-faint mx-auto mb-4" />
              <p className="text-gray-500 dark:text-graphite-muted">Žádné materiály nebyly nalezeny.</p>
            </div>
          )}
        </div>
      </div>

      {/* Pagination - Compact */}
      {totalCount > 0 && (
        <div className="flex-shrink-0 bg-white dark:bg-graphite-surface px-3 py-2 flex items-center justify-between border-t border-gray-200 dark:border-graphite-border text-xs">
          <div className="flex-1 flex justify-between sm:hidden">
            <button
              onClick={() => handlePageChange(pageNumber - 1)}
              disabled={pageNumber <= 1}
              className="relative inline-flex items-center px-2 py-1 border border-gray-300 dark:border-graphite-border text-xs font-medium rounded text-gray-700 dark:text-graphite-muted bg-white dark:bg-graphite-surface hover:bg-gray-50 dark:hover:bg-white/5 disabled:opacity-50 disabled:cursor-not-allowed"
            >
              Předchozí
            </button>
            <button
              onClick={() => handlePageChange(pageNumber + 1)}
              disabled={pageNumber >= totalPages}
              className="ml-2 relative inline-flex items-center px-2 py-1 border border-gray-300 dark:border-graphite-border text-xs font-medium rounded text-gray-700 dark:text-graphite-muted bg-white dark:bg-graphite-surface hover:bg-gray-50 dark:hover:bg-white/5 disabled:opacity-50 disabled:cursor-not-allowed"
            >
              Další
            </button>
          </div>
          <div className="hidden sm:flex-1 sm:flex sm:items-center sm:justify-between">
            <div className="flex items-center space-x-3">
              <p className="text-xs text-gray-600 dark:text-graphite-muted">
                {Math.min((pageNumber - 1) * pageSize + 1, totalCount)}-
                {Math.min(pageNumber * pageSize, totalCount)} z {totalCount}
                {productNameFilter || productCodeFilter ? (
                  <span className="text-gray-500 dark:text-graphite-muted"> (filtrováno)</span>
                ) : (
                  ""
                )}
              </p>
              <div className="flex items-center space-x-1">
                <span className="text-xs text-gray-600 dark:text-graphite-muted">Zobrazit:</span>
                <select
                  id="pageSize"
                  value={pageSize}
                  onChange={(e) => handlePageSizeChange(Number(e.target.value))}
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
                  onClick={() => handlePageChange(pageNumber - 1)}
                  disabled={pageNumber <= 1}
                  className="relative inline-flex items-center px-1 py-1 rounded-l border border-gray-300 dark:border-graphite-border bg-white dark:bg-graphite-surface text-xs font-medium text-gray-500 dark:text-graphite-muted hover:bg-gray-50 dark:hover:bg-white/5 disabled:opacity-50 disabled:cursor-not-allowed"
                >
                  <ChevronLeft className="h-3 w-3" />
                </button>

                {/* Page numbers */}
                {Array.from({ length: Math.min(totalPages, 5) }, (_, i) => {
                  let pageNum: number;
                  if (totalPages <= 5) {
                    pageNum = i + 1;
                  } else if (pageNumber <= 3) {
                    pageNum = i + 1;
                  } else if (pageNumber >= totalPages - 2) {
                    pageNum = totalPages - 4 + i;
                  } else {
                    pageNum = pageNumber - 2 + i;
                  }

                  return (
                    <button
                      key={pageNum}
                      onClick={() => handlePageChange(pageNum)}
                      className={`relative inline-flex items-center px-2 py-1 border text-xs font-medium ${
                        pageNum === pageNumber
                          ? "z-10 bg-indigo-50 dark:bg-graphite-accent/10 border-indigo-500 dark:border-graphite-accent text-indigo-600 dark:text-graphite-accent"
                          : "bg-white dark:bg-graphite-surface border-gray-300 dark:border-graphite-border text-gray-500 dark:text-graphite-muted hover:bg-gray-50 dark:hover:bg-white/5"
                      }`}
                    >
                      {pageNum}
                    </button>
                  );
                })}

                <button
                  onClick={() => handlePageChange(pageNumber + 1)}
                  disabled={pageNumber >= totalPages}
                  className="relative inline-flex items-center px-1 py-1 rounded-r border border-gray-300 dark:border-graphite-border bg-white dark:bg-graphite-surface text-xs font-medium text-gray-500 dark:text-graphite-muted hover:bg-gray-50 dark:hover:bg-white/5 disabled:opacity-50 disabled:cursor-not-allowed"
                >
                  <ChevronRight className="h-3 w-3" />
                </button>
              </nav>
            </div>
          </div>
        </div>
      )}

      {/* Modal for Product Detail */}
      <CatalogDetail
        item={selectedItem}
        isOpen={isDetailModalOpen}
        onClose={handleCloseDetail}
        defaultTab="basic"
      />

      {/* Modal for Inventory Management */}
      <ManufactureInventoryModal
        item={selectedInventoryItem}
        isOpen={isInventoryModalOpen}
        onClose={handleCloseInventory}
      />
    </div>
  );
};

export default ManufactureInventoryList;