import React, { useState, useEffect } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import {
  Search,
  Filter,
  AlertCircle,
  Loader2,
  ChevronUp,
  ChevronDown,
  ChevronLeft,
  ChevronRight,
  Package,
} from "lucide-react";
import {
  ProductType,
  CatalogItemDto,
} from "../../api/hooks/useCatalog";
import { useInventoryQuery } from "../../api/hooks/useInventory";
import CatalogDetail from "./CatalogDetail";
import InventoryModal from "../inventory/InventoryModal";
import { PAGE_CONTAINER_HEIGHT } from "../../constants/layout";
import { useScreenView } from "../../telemetry/useScreenView";

// Filter for inventory - only show finished goods that can be sold
const allowedInventoryTypes: ProductType[] = [
  ProductType.Product,
  ProductType.Goods,
  ProductType.Set,
];

const productTypeLabels: Record<ProductType, string> = {
  [ProductType.Product]: "Produkt",
  [ProductType.Goods]: "Zboží",
  [ProductType.Material]: "Materiál",
  [ProductType.SemiProduct]: "Polotovar",
  [ProductType.Set]: "Dárkový balíček",
  [ProductType.UNDEFINED]: "Nedefinováno",
};

const InventoryList: React.FC = () => {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();

  // Filter states - separate input values from applied filters
  const [productNameInput, setProductNameInput] = useState("");
  const [productCodeInput, setProductCodeInput] = useState("");
  const [productNameFilter, setProductNameFilter] = useState("");
  const [productCodeFilter, setProductCodeFilter] = useState("");
  // Start with the first allowed type instead of empty string to ensure data loads
  const [productTypeFilter, setProductTypeFilter] = useState<ProductType | "">(
    "",
  );

  // Pagination states
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(20);

  // Sorting states - default sort by lastInventoryDays descending
  const [sortBy, setSortBy] = useState<string>("lastInventoryDays");
  const [sortDescending, setSortDescending] = useState(true);

  // Modal states
  const [selectedItem, setSelectedItem] = useState<CatalogItemDto | null>(null);
  const [isDetailModalOpen, setIsDetailModalOpen] = useState(false);
  const [selectedInventoryItem, setSelectedInventoryItem] = useState<CatalogItemDto | null>(null);
  const [isInventoryModalOpen, setIsInventoryModalOpen] = useState(false);

  useScreenView('Logistics', 'InventoryFinishedGoods');

  // Use custom inventory hook that properly handles "all types" filtering
  const {
    data,
    isLoading: loading,
    error,
    refetch,
  } = useInventoryQuery(
    productNameFilter,
    productCodeFilter,
    productTypeFilter,
    pageNumber,
    pageSize,
    sortBy,
    sortDescending,
  );

  // Items are already filtered by the inventory hook
  const filteredItems = data?.items || [];
  
  const totalCount = data?.totalCount || 0;
  const totalPages = Math.ceil(totalCount / pageSize);

  // Handler for applying filters on Enter
  const handleApplyFilters = async () => {
    setProductNameFilter(productNameInput);
    setProductCodeFilter(productCodeInput);
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
    setProductTypeFilter("");
    setPageNumber(1);

    await refetch();
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

  // Process URL parameters on component mount
  useEffect(() => {
    const search = searchParams.get('search');
    const sortBy = searchParams.get('sortBy');
    const sortDescending = searchParams.get('sortDescending');
    const sort = searchParams.get('sort'); // Legacy format like "eshop_stock_asc"
    const type = searchParams.get('type');

    if (search) {
      setProductCodeInput(search);
      setProductCodeFilter(search);
    }

    if (sortBy) {
      setSortBy(sortBy);
    }

    if (sortDescending !== null) {
      setSortDescending(sortDescending === 'true');
    }

    // Handle legacy sort format like "eshop_stock_asc"
    if (sort && !sortBy) {
      if (sort === 'eshop_stock_asc') {
        setSortBy('eshop');
        setSortDescending(false);
      } else if (sort === 'eshop_stock_desc') {
        setSortBy('eshop');
        setSortDescending(true);
      }
      // Add more legacy formats as needed
    }

    if (type) {
      const productType = type as ProductType;
      if (Object.values(ProductType).includes(productType)) {
        setProductTypeFilter(productType);
      }
    }
  }, [searchParams]);

  // Reset pagination when type filter changes
  React.useEffect(() => {
    setPageNumber(1);
  }, [productTypeFilter]);

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

  // Handler for clicking transport quantity badge
  const handleTransportClick = (event: React.MouseEvent, productCode: string | undefined) => {
    event.stopPropagation(); // Prevent row click from triggering
    if (!productCode) return; // Guard against undefined productCode
    // Navigate to TransportBoxList with pre-filled filters
    navigate(`/logistics/transport-boxes?productCode=${encodeURIComponent(productCode)}&state=InTransit`);
  };

  // Handler for clicking reserve quantity badge
  const handleReserveClick = (event: React.MouseEvent, productCode: string | undefined) => {
    event.stopPropagation(); // Prevent row click from triggering
    if (!productCode) return; // Guard against undefined productCode
    // Navigate to TransportBoxList with pre-filled filters
    navigate(`/logistics/transport-boxes?productCode=${encodeURIComponent(productCode)}&state=Reserve`);
  };

  // Handler for clicking quarantine quantity badge
  const handleQuarantineClick = (event: React.MouseEvent, productCode: string | undefined) => {
    event.stopPropagation(); // Prevent row click from triggering
    if (!productCode) return; // Guard against undefined productCode
    // Navigate to TransportBoxList with pre-filled filters
    navigate(`/logistics/transport-boxes?productCode=${encodeURIComponent(productCode)}&state=Quarantine`);
  };

  // Sortable header component
  const SortableHeader: React.FC<{
    column: string;
    children: React.ReactNode;
  }> = ({ column, children }) => {
    const isActive = sortBy === column;
    const isAscending = isActive && !sortDescending;
    const isDescending = isActive && sortDescending;

    return (
      <th
        scope="col"
        className="px-6 py-4 text-center text-sm font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider cursor-pointer hover:bg-gray-100 dark:hover:bg-white/5 select-none"
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


  if (loading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="flex items-center space-x-2">
          <Loader2 className="h-5 w-5 animate-spin text-indigo-500" />
          <div className="text-gray-500 dark:text-graphite-muted">Načítání zásob...</div>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="flex items-center space-x-2 text-red-600 dark:text-red-400">
          <AlertCircle className="h-5 w-5" />
          <div>Chyba při načítání zásob: {error.message}</div>
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
          <Package className="h-6 w-6 text-indigo-600 dark:text-graphite-accent" />
          <h1 className="text-lg font-semibold text-gray-900 dark:text-graphite-text">Zásoby produktů</h1>
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
                <option key="all-types" value="">Všechny typy</option>
                {allowedInventoryTypes.map((productType) => (
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
      </div>

      {/* Data Grid - Scrollable */}
      <div className="flex-1 bg-white dark:bg-graphite-surface shadow dark:shadow-soft-dark rounded-lg overflow-hidden flex flex-col min-h-0">
        <div className="flex-1 overflow-auto">
          <table className="min-w-full divide-y divide-gray-200 dark:divide-graphite-border">
            <thead className="bg-gray-50 dark:bg-graphite-surface-2 sticky top-0 z-10">
              <tr>
                <SortableHeader column="productCode">
                  Kód produktu
                </SortableHeader>
                <SortableHeader column="productName">
                  Název produktu
                </SortableHeader>
                <th
                  scope="col"
                  className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider"
                >
                  Pozice
                </th>
                <SortableHeader column="lastInventoryDays">Posl. Inventura</SortableHeader>
                <SortableHeader column="available">Skladem</SortableHeader>
                <SortableHeader column="transport">Transport</SortableHeader>
                <SortableHeader column="reserve">Rezerva</SortableHeader>
                <SortableHeader column="quarantine">Karanténa</SortableHeader>
                <th
                  scope="col"
                  className="px-6 py-4 text-center text-sm font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider"
                >
                  Celkem
                </th>

              </tr>
            </thead>
            <tbody className="bg-white dark:bg-graphite-surface divide-y divide-gray-200 dark:divide-graphite-border">
              {filteredItems.map((item) => {
                const available = Math.round((item.stock?.eshop || 0) * 100) / 100;
                const transport = Math.round((item.stock?.transport || 0) * 100) / 100;
                const reserve = Math.round((item.stock?.reserve || 0) * 100) / 100;
                const quarantine = Math.round((item.stock?.quarantine || 0) * 100) / 100;
                const total = Math.round(((item.stock?.available || 0) + (item.stock?.reserve || 0)) * 100) / 100;

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
                        title="Klikněte pro inventarizaci"
                      >
                          {item.productCode}
                      </span>
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900 dark:text-graphite-text">
                      {item.productName}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500 dark:text-graphite-muted">
                      {item.location || "-"}
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
                    <td className="px-6 py-5 whitespace-nowrap text-center">
                      <span 
                        className="inline-flex items-center px-4 py-2 rounded-full text-base font-semibold bg-green-100 dark:bg-emerald-900/30 text-green-800 dark:text-emerald-300 justify-center inventory-badge hover:bg-green-200 dark:hover:bg-emerald-900/50 hover:text-green-900 dark:hover:text-emerald-200 cursor-pointer"
                        title="Klikněte pro inventarizaci"
                      >
                        {available}
                      </span>
                    </td>
                    <td className="px-6 py-5 whitespace-nowrap text-center">
                      {transport > 0 ? (
                        <span 
                          className="inline-flex items-center px-4 py-2 rounded-full text-base font-semibold bg-blue-100 dark:bg-blue-900/30 text-blue-800 dark:text-blue-300 justify-center inventory-badge hover:bg-blue-200 dark:hover:bg-blue-900/50 hover:text-blue-900 dark:hover:text-blue-200"
                          onClick={(e) => handleTransportClick(e, item.productCode)}
                          title="Klikněte pro zobrazení přepravy"
                        >
                          {transport}
                        </span>
                      ) : (
                        <span className="text-gray-400 dark:text-graphite-faint text-base">-</span>
                      )}
                    </td>
                    <td className="px-6 py-5 whitespace-nowrap text-center">
                      {reserve > 0 ? (
                        <span
                          className="inline-flex items-center px-4 py-2 rounded-full text-base font-semibold bg-amber-100 dark:bg-amber-900/30 text-amber-800 dark:text-amber-300 justify-center inventory-badge hover:bg-amber-200 dark:hover:bg-amber-900/50 hover:text-amber-900 dark:hover:text-amber-200"
                          onClick={(e) => handleReserveClick(e, item.productCode)}
                          title="Klikněte pro zobrazení rezervy"
                        >
                          {reserve}
                        </span>
                      ) : (
                        <span className="text-gray-400 dark:text-graphite-faint text-base">-</span>
                      )}
                    </td>
                    <td className="px-6 py-5 whitespace-nowrap text-center">
                      {quarantine > 0 ? (
                        <span
                          className="inline-flex items-center px-4 py-2 rounded-full text-base font-semibold bg-orange-100 dark:bg-orange-900/30 text-orange-800 dark:text-orange-300 justify-center inventory-badge hover:bg-orange-200 dark:hover:bg-orange-900/50 hover:text-orange-900 dark:hover:text-orange-200"
                          onClick={(e) => handleQuarantineClick(e, item.productCode)}
                          title="Klikněte pro zobrazení karantény"
                        >
                          {quarantine}
                        </span>
                      ) : (
                        <span className="text-gray-400 dark:text-graphite-faint text-base">-</span>
                      )}
                    </td>
                    <td className="px-6 py-5 whitespace-nowrap text-center">
                      <span className="inline-flex items-center px-4 py-2 rounded-full text-base font-semibold bg-purple-100 dark:bg-purple-900/30 text-purple-800 dark:text-purple-300 justify-center inventory-badge">
                        {total}
                      </span>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>

          {filteredItems.length === 0 && (
            <div className="text-center py-8">
              <Package className="h-12 w-12 text-gray-400 dark:text-graphite-faint mx-auto mb-4" />
              <p className="text-gray-500 dark:text-graphite-muted">Žádné zásoby nebyly nalezeny.</p>
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
                  <option key="10" value={10}>10</option>
                  <option key="20" value={20}>20</option>
                  <option key="50" value={50}>50</option>
                  <option key="100" value={100}>100</option>
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
      <InventoryModal
        item={selectedInventoryItem}
        isOpen={isInventoryModalOpen}
        onClose={handleCloseInventory}
      />
    </div>
  );
};

export default InventoryList;