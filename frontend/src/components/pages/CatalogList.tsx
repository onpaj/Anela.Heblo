import React, { useState, useEffect } from "react";
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
} from "lucide-react";
import {
  useCatalogQuery,
  ProductType,
  CatalogItemDto,
} from "../../api/hooks/useCatalog";
import CatalogDetail from "./CatalogDetail";
import { PAGE_CONTAINER_HEIGHT } from "../../constants/layout";

const productTypeLabels: Record<ProductType, string> = {
  [ProductType.Product]: "Produkt",
  [ProductType.Goods]: "Zboží",
  [ProductType.Material]: "Materiál",
  [ProductType.SemiProduct]: "Polotovar",
  [ProductType.Set]: "Dárkový balíček",
  [ProductType.UNDEFINED]: "Nedefinováno",
};

const productTypeOptions = [
  { value: ProductType.Product, label: "Produkt" },
  { value: ProductType.Goods, label: "Zboží" },
  { value: ProductType.Material, label: "Materiál" },
  { value: ProductType.SemiProduct, label: "Polotovar" },
  { value: ProductType.Set, label: "Dárkový balíček" },
  { value: ProductType.UNDEFINED, label: "Nedefinováno" },
];

const CatalogList: React.FC = () => {
  // URL search params for filter state management
  const [searchParams, setSearchParams] = useSearchParams();

  // Initialize filters from URL params
  const getInitialProductNameFilter = () => searchParams.get("productName") || "";
  const getInitialProductCodeFilter = () => searchParams.get("productCode") || "";
  const getInitialProductTypeFilter = () => {
    const typeParam = searchParams.get("productType");
    return typeParam && Object.values(ProductType).includes(typeParam as ProductType)
      ? (typeParam as ProductType)
      : "";
  };
  const getInitialPageNumber = () => {
    const pageParam = searchParams.get("page");
    return pageParam ? parseInt(pageParam, 10) : 1;
  };
  const getInitialPageSize = () => {
    const sizeParam = searchParams.get("pageSize");
    return sizeParam ? parseInt(sizeParam, 10) : 20;
  };
  const getInitialSortBy = () => searchParams.get("sortBy") || "";
  const getInitialSortDescending = () => searchParams.get("sortDesc") === "true";


  // Filter states - separate input values from applied filters
  const [productNameInput, setProductNameInput] = useState(getInitialProductNameFilter);
  const [productCodeInput, setProductCodeInput] = useState(getInitialProductCodeFilter);
  const [productNameFilter, setProductNameFilter] = useState(getInitialProductNameFilter);
  const [productCodeFilter, setProductCodeFilter] = useState(getInitialProductCodeFilter);
  const [productTypeFilter, setProductTypeFilter] = useState<ProductType | "">(
    getInitialProductTypeFilter,
  );

  // Pagination states
  const [pageNumber, setPageNumber] = useState(getInitialPageNumber);
  const [pageSize, setPageSize] = useState(getInitialPageSize);

  // Sorting states
  const [sortBy, setSortBy] = useState<string>(getInitialSortBy);
  const [sortDescending, setSortDescending] = useState(getInitialSortDescending);

  // Modal states
  const [selectedItem, setSelectedItem] = useState<CatalogItemDto | null>(null);
  const [isDetailModalOpen, setIsDetailModalOpen] = useState(false);

  // Use the actual API call
  const {
    data,
    isLoading: loading,
    error,
    refetch,
  } = useCatalogQuery(
    productNameFilter,
    productCodeFilter,
    productTypeFilter,
    pageNumber,
    pageSize,
    sortBy,
    sortDescending,
  );

  const filteredItems = data?.items || [];
  const totalCount = data?.totalCount || 0; // Total count from API
  const totalPages = Math.ceil(totalCount / pageSize);

  // Handler for applying filters on Enter
  const handleApplyFilters = async () => {
    setProductNameFilter(productNameInput);
    setProductCodeFilter(productCodeInput);
    setPageNumber(1); // Reset to first page when applying filters

    // Update URL params immediately to prevent race condition with useEffect
    const params = new URLSearchParams(searchParams);
    if (productNameInput) {
      params.set("productName", productNameInput);
    } else {
      params.delete("productName");
    }
    if (productCodeInput) {
      params.set("productCode", productCodeInput);
    } else {
      params.delete("productCode");
    }
    // Remove page param when resetting to page 1
    params.delete("page");
    setSearchParams(params, { replace: true });

    // Force data reload by refetching
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
    setPageNumber(1); // Reset to first page when clearing filters

    // Update URL params immediately to prevent race condition with useEffect
    const params = new URLSearchParams(searchParams);
    params.delete("productName");
    params.delete("productCode");
    params.delete("productType");
    params.delete("page");
    setSearchParams(params, { replace: true });

    // Force data reload by refetching
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
    setPageNumber(1); // Reset to page 1 when sort changes
    // React Query will automatically refetch when sortBy or sortDescending changes
  };

  // Pagination handlers
  const handlePageChange = (newPage: number) => {
    if (newPage >= 1 && newPage <= totalPages) {
      setPageNumber(newPage);
      // React Query will automatically refetch when pageNumber changes
    }
  };

  const handlePageSizeChange = (newPageSize: number) => {
    setPageSize(newPageSize);
    setPageNumber(1); // Reset to first page when changing page size
    // React Query will automatically refetch when pageSize changes
  };

  // Synchronize filter state with URL parameters when state changes
  useEffect(() => {
    const params = new URLSearchParams();

    // Add filters to URL params
    if (productNameFilter) params.set("productName", productNameFilter);
    if (productCodeFilter) params.set("productCode", productCodeFilter);
    if (productTypeFilter) params.set("productType", productTypeFilter);

    // Add pagination to URL params
    if (pageNumber !== 1) params.set("page", pageNumber.toString());
    if (pageSize !== 20) params.set("pageSize", pageSize.toString());

    // Add sorting to URL params
    if (sortBy) params.set("sortBy", sortBy);
    if (sortDescending) params.set("sortDesc", "true");

    // Update URL without causing navigation
    setSearchParams(params, { replace: true });
  }, [productNameFilter, productCodeFilter, productTypeFilter, pageNumber, pageSize, sortBy, sortDescending, setSearchParams]);

  // Handle browser back/forward navigation by reading URL params
  // This effect runs when searchParams changes (e.g., via browser back/forward)
  useEffect(() => {
    const newProductName = searchParams.get("productName") || "";
    const newProductCode = searchParams.get("productCode") || "";
    const newProductType = searchParams.get("productType") || "";
    const newPage = searchParams.get("page") ? parseInt(searchParams.get("page")!, 10) : 1;
    const newPageSize = searchParams.get("pageSize") ? parseInt(searchParams.get("pageSize")!, 10) : 20;
    const newSortBy = searchParams.get("sortBy") || "";
    const newSortDesc = searchParams.get("sortDesc") === "true";

    // Only update state if values actually changed from URL
    // This prevents infinite loops where state update triggers URL update triggers state update
    if (
      newProductName !== productNameFilter ||
      newProductCode !== productCodeFilter ||
      newProductType !== productTypeFilter ||
      newPage !== pageNumber ||
      newPageSize !== pageSize ||
      newSortBy !== sortBy ||
      newSortDesc !== sortDescending
    ) {
      // Update both input and filter state from URL params
      setProductNameInput(newProductName);
      setProductNameFilter(newProductName);
      setProductCodeInput(newProductCode);
      setProductCodeFilter(newProductCode);
      setProductTypeFilter(newProductType as ProductType | "");
      setPageNumber(newPage);
      setPageSize(newPageSize);
      setSortBy(newSortBy);
      setSortDescending(newSortDesc);
    }
    // Only depend on searchParams to avoid infinite loops
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [searchParams]);

  // Sync page number state from URL parameter changes (for external URL manipulation)
  React.useEffect(() => {
    const urlPageParam = searchParams.get("page");
    const urlPageNumber = urlPageParam ? parseInt(urlPageParam, 10) : 1;
    const validPageNumber =
      isNaN(urlPageNumber) || urlPageNumber < 1 ? 1 : urlPageNumber;

    if (validPageNumber !== pageNumber) {
      setPageNumber(validPageNumber);
    }
  }, [searchParams, pageNumber]);

  // Sync URL parameter with page number state
  React.useEffect(() => {
    const currentPage = searchParams.get("page");
    const currentPageNumber = currentPage ? parseInt(currentPage, 10) : 1;

    if (pageNumber === 1) {
      // Remove page parameter when on page 1
      if (currentPage) {
        const newParams = new URLSearchParams(searchParams);
        newParams.delete("page");
        setSearchParams(newParams, { replace: true });
      }
    } else if (currentPageNumber !== pageNumber) {
      // Update page parameter when page number changes
      const newParams = new URLSearchParams(searchParams);
      newParams.set("page", pageNumber.toString());
      setSearchParams(newParams, { replace: true });
    }
  }, [pageNumber, searchParams, setSearchParams]);

  // Modal handlers
  const handleItemClick = (item: CatalogItemDto) => {
    setSelectedItem(item);
    setIsDetailModalOpen(true);
  };

  const handleCloseDetail = () => {
    setIsDetailModalOpen(false);
    setSelectedItem(null);
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
        className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider cursor-pointer hover:bg-gray-100 select-none"
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

  if (loading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="flex items-center space-x-2">
          <Loader2 className="h-5 w-5 animate-spin text-indigo-500" />
          <div className="text-gray-500">Načítání katalogu...</div>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="flex items-center space-x-2 text-red-600">
          <AlertCircle className="h-5 w-5" />
          <div>Chyba při načítání katalogu: {error.message}</div>
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
        <h1 className="text-lg font-semibold text-gray-900">Seznam produktů</h1>
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
                  placeholder="Název produktu..."
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
                  placeholder="Kód produktu..."
                />
              </div>
            </div>

            <div className="flex-1 max-w-xs">
              <select
                id="productType"
                value={productTypeFilter}
                onChange={(e) => {
                  const newType = e.target.value === ""
                    ? ""
                    : (e.target.value as ProductType);
                  setProductTypeFilter(newType);
                  setPageNumber(1); // Reset to first page when filter changes

                  // Update URL params immediately to prevent race condition with useEffect
                  const params = new URLSearchParams(searchParams);
                  if (newType) {
                    params.set("productType", newType);
                  } else {
                    params.delete("productType");
                  }
                  params.delete("page"); // Remove page param when resetting to page 1
                  setSearchParams(params, { replace: true });
                }}
                className="block w-full pl-3 pr-10 py-2 text-base border-gray-300 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm rounded-md"
              >
                <option value="">Všechny typy</option>
                {productTypeOptions.map((option) => (
                  <option key={option.value} value={option.value}>
                    {option.label}
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
                  Kód produktu
                </SortableHeader>
                <SortableHeader column="productName">
                  Název produktu
                </SortableHeader>
                <SortableHeader column="type">Typ</SortableHeader>
                <SortableHeader column="available">Dostupné</SortableHeader>
                <SortableHeader column="reserve">V rezervě</SortableHeader>
                <th
                  scope="col"
                  className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider"
                >
                  Umístění
                </th>
                <th
                  scope="col"
                  className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider"
                >
                  MOQ
                </th>
                <th
                  scope="col"
                  className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider"
                >
                  MMQ
                </th>
              </tr>
            </thead>
            <tbody className="bg-white divide-y divide-gray-200">
              {filteredItems.map((item) => (
                <tr
                  key={item.productCode}
                  className="hover:bg-gray-50 cursor-pointer transition-colors duration-150"
                  onClick={() => handleItemClick(item)}
                  title="Klikněte pro zobrazení detailu"
                >
                  <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900">
                    {item.productCode}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                    {item.productName}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                    <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-gray-100 text-gray-800">
                      {productTypeLabels[item.type || ProductType.UNDEFINED]}
                    </span>
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-center">
                    <span className="inline-flex items-center px-3 py-1 rounded-full text-sm font-semibold bg-green-100 text-green-800">
                      {Math.round((item.stock?.available || 0) * 100) / 100}
                    </span>
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-center">
                    {(item.stock?.reserve || 0) > 0 ? (
                      <span className="inline-flex items-center px-3 py-1 rounded-full text-sm font-semibold bg-amber-100 text-amber-800">
                        {Math.round((item.stock?.reserve || 0) * 100) / 100}
                      </span>
                    ) : (
                      <span className="text-gray-400">-</span>
                    )}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                    {item.location}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                    {item.minimalOrderQuantity}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                    {item.minimalManufactureQuantity}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>

          {filteredItems.length === 0 && (
            <div className="text-center py-8">
              <p className="text-gray-500">Žádné produkty nebyly nalezeny.</p>
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
    </div>
  );
};

export default CatalogList;
