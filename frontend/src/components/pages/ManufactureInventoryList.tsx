import React, { useState } from "react";
import { useNavigate } from "react-router-dom";
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
import ManufactureInventoryModal from "../inventory/ManufactureInventoryModal";
import { PAGE_CONTAINER_HEIGHT } from "../../constants/layout";

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

const ManufactureInventoryList: React.FC = () => {
  const navigate = useNavigate();
  
  // Filter states - separate input values from applied filters
  const [productNameInput, setProductNameInput] = useState("");
  const [productCodeInput, setProductCodeInput] = useState("");
  const [productNameFilter, setProductNameFilter] = useState("");
  const [productCodeFilter, setProductCodeFilter] = useState("");
  // Start with Material as the default type for manufacture inventory
  const [productTypeFilter, setProductTypeFilter] = useState<ProductType | "">(
    ProductType.Material,
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
  );

  // Items are already filtered by the manufacture inventory hook
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
    setProductTypeFilter(ProductType.Material);
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
        className={`px-6 py-4 ${alignmentClass} text-sm font-medium text-gray-500 uppercase tracking-wider cursor-pointer hover:bg-gray-100 select-none`}
        onClick={() => handleSort(column)}
      >
        <div className={`flex items-center space-x-1 ${align === 'center' ? 'justify-center' : 'justify-start'}`}>
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


  if (loading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="flex items-center space-x-2">
          <Loader2 className="h-5 w-5 animate-spin text-indigo-500" />
          <div className="text-gray-500">Načítání zásob materiálů...</div>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="flex items-center space-x-2 text-red-600">
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
          <Wrench className="h-6 w-6 text-indigo-600" />
          <h1 className="text-lg font-semibold text-gray-900">Zásoby materiálů</h1>
        </div>
      </div>

      {/* Filters - Fixed */}
      <div className="flex-shrink-0 bg-white shadow rounded-lg p-4 mb-4">
        <div className="flex items-center justify-between flex-wrap gap-3">
          <div className="flex items-center gap-3 flex-1 min-w-0">
            <div className="flex items-center">
              <Filter className="h-4 w-4 text-gray-400 mr-2" />
              <span className="text-sm font-medium text-gray-900">Filtry:</span>
            </div>

            <div className="flex-1 max-w-xs">
              <div className="relative">
                <div className="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none">
                  <Search className="h-4 w-4 text-gray-400" />
                </div>
                <input
                  type="text"
                  id="productName"
                  value={productNameInput}
                  onChange={(e) => setProductNameInput(e.target.value)}
                  onKeyDown={handleKeyDown}
                  className="focus:ring-indigo-500 focus:border-indigo-500 block w-full pl-10 pr-3 py-2 sm:text-sm border-gray-300 rounded-md"
                  placeholder="Název materiálu..."
                />
              </div>
            </div>

            <div className="flex-1 max-w-xs">
              <div className="relative">
                <div className="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none">
                  <Search className="h-4 w-4 text-gray-400" />
                </div>
                <input
                  type="text"
                  id="productCode"
                  value={productCodeInput}
                  onChange={(e) => setProductCodeInput(e.target.value)}
                  onKeyDown={handleKeyDown}
                  className="focus:ring-indigo-500 focus:border-indigo-500 block w-full pl-10 pr-3 py-2 sm:text-sm border-gray-300 rounded-md"
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
                className="block w-full pl-3 pr-10 py-2 text-base border-gray-300 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm rounded-md"
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
      </div>

      {/* Data Grid - Scrollable */}
      <div className="flex-1 bg-white shadow rounded-lg overflow-hidden flex flex-col min-h-0">
        <div className="flex-1 overflow-auto">
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-gray-50 sticky top-0 z-10">
              <tr>
                <SortableHeader column="productCode">
                  Kód materiálu
                </SortableHeader>
                <SortableHeader column="productName">
                  Název materiálu
                </SortableHeader>
                <SortableHeader column="lastInventoryDays" align="center">Posl. Inventura</SortableHeader>
                <SortableHeader column="available" align="center">Skladem</SortableHeader>

              </tr>
            </thead>
            <tbody className="bg-white divide-y divide-gray-200">
              {filteredItems.map((item) => {
                const available = Math.round((item.stock?.available || 0) * 100) / 100;

                return (
                  <tr
                    key={item.productCode}
                    className="hover:bg-gray-50 cursor-pointer transition-colors duration-150"
                    onClick={() => handleInventoryClick(item)}
                    title="Klikněte pro zobrazení detailu"
                  >
                    <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900">
                      <span 
                        onClick={(e) => handleItemClick(e, item)}
                        title="Klikněte pro inventarizaci materiálu"
                      >
                        {item.productCode}
                      </span>
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                      {item.productName}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-center">
                      {item.lastStockTaking ? (
                        <div 
                          className="text-sm text-gray-700 cursor-help inline-block"
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
                        <span className="text-gray-400 text-sm">-</span>
                      )}
                    </td>
                    <td className="px-6 py-5 whitespace-nowrap text-center">
                      <span 
                        className="inline-flex items-center px-4 py-2 rounded-full text-base font-semibold bg-green-100 text-green-800 justify-center inventory-badge hover:bg-green-200 hover:text-green-900 cursor-pointer"
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
              <Wrench className="h-12 w-12 text-gray-400 mx-auto mb-4" />
              <p className="text-gray-500">Žádné materiály nebyly nalezeny.</p>
            </div>
          )}
        </div>
      </div>

      {/* Pagination - Compact */}
      {totalCount > 0 && (
        <div className="flex-shrink-0 bg-white px-3 py-2 flex items-center justify-between border-t border-gray-200 text-xs">
          <div className="flex-1 flex justify-between sm:hidden">
            <button
              onClick={() => handlePageChange(pageNumber - 1)}
              disabled={pageNumber <= 1}
              className="relative inline-flex items-center px-2 py-1 border border-gray-300 text-xs font-medium rounded text-gray-700 bg-white hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
            >
              Předchozí
            </button>
            <button
              onClick={() => handlePageChange(pageNumber + 1)}
              disabled={pageNumber >= totalPages}
              className="ml-2 relative inline-flex items-center px-2 py-1 border border-gray-300 text-xs font-medium rounded text-gray-700 bg-white hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
            >
              Další
            </button>
          </div>
          <div className="hidden sm:flex-1 sm:flex sm:items-center sm:justify-between">
            <div className="flex items-center space-x-3">
              <p className="text-xs text-gray-600">
                {Math.min((pageNumber - 1) * pageSize + 1, totalCount)}-
                {Math.min(pageNumber * pageSize, totalCount)} z {totalCount}
                {productNameFilter || productCodeFilter ? (
                  <span className="text-gray-500"> (filtrováno)</span>
                ) : (
                  ""
                )}
              </p>
              <div className="flex items-center space-x-1">
                <span className="text-xs text-gray-600">Zobrazit:</span>
                <select
                  id="pageSize"
                  value={pageSize}
                  onChange={(e) => handlePageSizeChange(Number(e.target.value))}
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
                  onClick={() => handlePageChange(pageNumber - 1)}
                  disabled={pageNumber <= 1}
                  className="relative inline-flex items-center px-1 py-1 rounded-l border border-gray-300 bg-white text-xs font-medium text-gray-500 hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
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
                          ? "z-10 bg-indigo-50 border-indigo-500 text-indigo-600"
                          : "bg-white border-gray-300 text-gray-500 hover:bg-gray-50"
                      }`}
                    >
                      {pageNum}
                    </button>
                  );
                })}

                <button
                  onClick={() => handlePageChange(pageNumber + 1)}
                  disabled={pageNumber >= totalPages}
                  className="relative inline-flex items-center px-1 py-1 rounded-r border border-gray-300 bg-white text-xs font-medium text-gray-500 hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
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