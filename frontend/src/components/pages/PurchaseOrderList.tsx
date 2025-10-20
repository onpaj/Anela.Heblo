import React, { useState } from "react";
import {
  Search,
  Filter,
  AlertCircle,
  Loader2,
  ChevronUp,
  ChevronDown,
  ChevronLeft,
  ChevronRight,
  Plus,
  Calendar,
  Check,
} from "lucide-react";
import { useSearchParams } from "react-router-dom";
import {
  usePurchaseOrdersQuery,
  GetPurchaseOrdersRequest,
} from "../../api/hooks/usePurchaseOrders";
import PurchaseOrderDetail from "./PurchaseOrderDetail";
import PurchaseOrderForm from "./PurchaseOrderForm";
import { PAGE_CONTAINER_HEIGHT } from "../../constants/layout";

// Status labels mapping
const statusLabels: Record<string, string> = {
  Draft: "Návrh",
  InTransit: "V přepravě",
  Completed: "Dokončeno",
};

const statusColors: Record<string, string> = {
  Draft: "bg-gray-100 text-gray-800",
  InTransit: "bg-blue-100 text-blue-800",
  Completed: "bg-green-100 text-green-800",
};

const PurchaseOrderList: React.FC = () => {
  const [searchParams] = useSearchParams();
  
  // Initialize from URL parameters
  const stateParam = searchParams.get('state');
  const initialStatus = stateParam === 'InTransit' ? 'InTransit' : 'ActiveOnly';
  
  // Filter states - separate input values from applied filters
  const [searchTermInput, setSearchTermInput] = useState(searchParams.get('searchTerm') || "");
  const [statusInput, setStatusInput] = useState(initialStatus);
  const [fromDateInput, setFromDateInput] = useState(searchParams.get('fromDate') || "");
  const [toDateInput, setToDateInput] = useState(searchParams.get('toDate') || "");

  const [searchTermFilter, setSearchTermFilter] = useState(searchParams.get('searchTerm') || "");
  const [statusFilter, setStatusFilter] = useState(initialStatus);
  const [fromDateFilter, setFromDateFilter] = useState(searchParams.get('fromDate') || "");
  const [toDateFilter, setToDateFilter] = useState(searchParams.get('toDate') || "");

  // Pagination states
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(20);

  // Sorting states
  const [sortBy, setSortBy] = useState("OrderDate");
  const [sortDescending, setSortDescending] = useState(true);

  // Modal states
  const [selectedOrderId, setSelectedOrderId] = useState<number | null>(null);
  const [isDetailModalOpen, setIsDetailModalOpen] = useState(false);
  const [isCreateModalOpen, setIsCreateModalOpen] = useState(false);
  const [isEditModalOpen, setIsEditModalOpen] = useState(false);
  const [editOrderId, setEditOrderId] = useState<number | null>(null);

  // Build request object
  const request: GetPurchaseOrdersRequest = {
    searchTerm: searchTermFilter || undefined,
    status:
      statusFilter === "ActiveOnly" ? undefined : statusFilter || undefined,
    fromDate: fromDateFilter ? new Date(fromDateFilter) : undefined,
    toDate: toDateFilter ? new Date(toDateFilter) : undefined,
    activeOrdersOnly: statusFilter === "ActiveOnly" ? true : false,
    pageNumber,
    pageSize,
    sortBy,
    sortDescending,
  };

  // Use the API query
  const {
    data,
    isLoading: loading,
    error,
    refetch,
  } = usePurchaseOrdersQuery(request);

  const orders = data?.orders || [];
  const totalCount = data?.totalCount || 0;
  const totalPages = data?.totalPages || 0;

  // Handler for applying filters on Enter or button click
  const handleApplyFilters = async () => {
    setSearchTermFilter(searchTermInput);
    setStatusFilter(statusInput);
    setFromDateFilter(fromDateInput);
    setToDateFilter(toDateInput);
    setPageNumber(1); // Reset to first page when applying filters

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
    setSearchTermInput("");
    setStatusInput("ActiveOnly"); // Reset to default (ActiveOnly)
    setFromDateInput("");
    setToDateInput("");
    setSearchTermFilter("");
    setStatusFilter("ActiveOnly"); // Reset to default (ActiveOnly)
    setFromDateFilter("");
    setToDateFilter("");
    setPageNumber(1);

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

  // Modal handlers
  const handleOrderClick = (orderId: number) => {
    setSelectedOrderId(orderId);
    setIsDetailModalOpen(true);
  };

  const handleCloseDetail = () => {
    setIsDetailModalOpen(false);
    setSelectedOrderId(null);
  };

  const handleCreateOrder = () => {
    setIsCreateModalOpen(true);
  };

  const handleCloseCreate = () => {
    setIsCreateModalOpen(false);
  };

  const handleEditOrder = (orderId: number) => {
    setEditOrderId(orderId);
    setSelectedOrderId(orderId); // Ensure selected order is set for return to detail
    setIsEditModalOpen(true);
    setIsDetailModalOpen(false); // Close detail modal when opening edit
  };

  const handleCloseEdit = () => {
    setIsEditModalOpen(false);
    // If we have a selected order, return to detail view instead of closing everything
    if (selectedOrderId) {
      setIsDetailModalOpen(true);
    }
    setEditOrderId(null);
  };

  const handleEditSuccess = (orderId: number) => {
    // Refresh the list
    refetch();
    // If we have a selected order, return to detail view after successful edit
    if (selectedOrderId) {
      setIsDetailModalOpen(true);
    }
    console.log("Order updated successfully:", orderId);
  };

  const handleCreateSuccess = (orderId: number) => {
    // Refresh the list
    refetch();
    // Optionally open the detail of the newly created order
    console.log("Order created successfully:", orderId);
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

  // Format date for display
  const formatDate = (date: Date | string | undefined) => {
    if (!date) return "-";
    const dateObj = typeof date === "string" ? new Date(date) : date;
    return dateObj.toLocaleDateString("cs-CZ");
  };

  // Format currency
  const formatCurrency = (amount: number) => {
    return `${amount.toLocaleString("cs-CZ", { minimumFractionDigits: 2, maximumFractionDigits: 2 })} Kč`;
  };

  if (loading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="flex items-center space-x-2">
          <Loader2 className="h-5 w-5 animate-spin text-indigo-500" />
          <div className="text-gray-500">Načítání objednávek...</div>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="flex items-center space-x-2 text-red-600">
          <AlertCircle className="h-5 w-5" />
          <div>Chyba při načítání objednávek: {error.message}</div>
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
      <div className="flex-shrink-0 mb-3 flex items-center justify-between">
        <h1 className="text-lg font-semibold text-gray-900">
          Nákupní objednávky
        </h1>
        <button
          onClick={handleCreateOrder}
          className="bg-indigo-600 hover:bg-indigo-700 text-white font-medium py-2 px-4 rounded-md transition-colors duration-200 text-sm flex items-center gap-2"
        >
          <Plus className="h-4 w-4" />
          Nová objednávka
        </button>
      </div>

      {/* Filters - Fixed */}
      <div className="flex-shrink-0 bg-white shadow rounded-lg p-4 mb-4">
        <div className="flex items-center justify-between flex-wrap gap-3">
          <div className="flex items-center gap-3 flex-1 min-w-0">
            <div className="flex items-center">
              <Filter className="h-4 w-4 text-gray-400 mr-2" />
              <span className="text-sm font-medium text-gray-900">Filtry:</span>
            </div>

            {/* Search term */}
            <div className="flex-1 max-w-xs">
              <div className="relative">
                <div className="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none">
                  <Search className="h-4 w-4 text-gray-400" />
                </div>
                <input
                  type="text"
                  value={searchTermInput}
                  onChange={(e) => setSearchTermInput(e.target.value)}
                  onKeyDown={handleKeyDown}
                  className="focus:ring-indigo-500 focus:border-indigo-500 block w-full pl-10 pr-3 py-2 sm:text-sm border-gray-300 rounded-md"
                  placeholder="Hledat objednávky..."
                />
              </div>
            </div>

            {/* Status filter */}
            <div className="flex-1 max-w-xs">
              <select
                value={statusInput}
                onChange={(e) => setStatusInput(e.target.value)}
                className="block w-full pl-3 pr-10 py-2 text-base border-gray-300 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm rounded-md"
              >
                <option value="ActiveOnly">Jen aktivní</option>
                <option value="">Všechny stavy</option>
                <option value="Draft">Návrh</option>
                <option value="InTransit">V přepravě</option>
                <option value="Completed">Dokončeno</option>
              </select>
            </div>

            {/* Date range */}
            <div className="flex items-center gap-2">
              <div className="relative">
                <Calendar className="absolute left-3 top-1/2 transform -translate-y-1/2 h-4 w-4 text-gray-400" />
                <input
                  type="date"
                  value={fromDateInput}
                  onChange={(e) => setFromDateInput(e.target.value)}
                  className="pl-10 pr-3 py-2 text-sm border-gray-300 rounded-md focus:ring-indigo-500 focus:border-indigo-500"
                  placeholder="Od"
                />
              </div>
              <span className="text-gray-500">-</span>
              <div className="relative">
                <Calendar className="absolute left-3 top-1/2 transform -translate-y-1/2 h-4 w-4 text-gray-400" />
                <input
                  type="date"
                  value={toDateInput}
                  onChange={(e) => setToDateInput(e.target.value)}
                  className="pl-10 pr-3 py-2 text-sm border-gray-300 rounded-md focus:ring-indigo-500 focus:border-indigo-500"
                  placeholder="Do"
                />
              </div>
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
                <SortableHeader column="OrderNumber">
                  Číslo objednávky
                </SortableHeader>
                <th
                  scope="col"
                  className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider"
                >
                  Dodavatel
                </th>
                <SortableHeader column="OrderDate">
                  Datum objednávky
                </SortableHeader>
                <SortableHeader column="ExpectedDeliveryDate">
                  Plánované dodání
                </SortableHeader>
                <SortableHeader column="Status">Stav</SortableHeader>
                <th
                  scope="col"
                  className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider"
                >
                  Faktura
                </th>
                <SortableHeader column="TotalAmount">
                  Celková částka
                </SortableHeader>
                <th
                  scope="col"
                  className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider"
                >
                  Položky
                </th>
              </tr>
            </thead>
            <tbody className="bg-white divide-y divide-gray-200">
              {orders.map((order) => (
                <tr
                  key={order.id}
                  className="hover:bg-gray-50 cursor-pointer transition-colors duration-150"
                  onClick={() => order.id && handleOrderClick(order.id)}
                  title="Klikněte pro zobrazení detailu"
                >
                  <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900">
                    {order.orderNumber}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                    {order.supplierName}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                    {formatDate(order.orderDate)}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                    {order.expectedDeliveryDate
                      ? formatDate(order.expectedDeliveryDate)
                      : "-"}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                    <span
                      className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${(order.status && statusColors[order.status]) || "bg-gray-100 text-gray-800"}`}
                    >
                      {(order.status && statusLabels[order.status]) ||
                        order.status}
                    </span>
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                    <div className="flex items-center justify-center">
                      {order.invoiceAcquired ? (
                        <div className="flex items-center text-green-600">
                          <Check className="h-4 w-4 mr-1" />
                          <span className="text-xs font-medium">Ano</span>
                        </div>
                      ) : (
                        <span className="text-xs text-gray-400">Ne</span>
                      )}
                    </div>
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900 font-medium">
                    {formatCurrency(order.totalAmount || 0)}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500 text-center">
                    {order.lineCount}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>

          {orders.length === 0 && (
            <div className="text-center py-8">
              <p className="text-gray-500">Žádné objednávky nebyly nalezeny.</p>
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
                {searchTermFilter ||
                (statusFilter && statusFilter !== "ActiveOnly") ||
                fromDateFilter ||
                toDateFilter ? (
                  <span className="text-gray-500"> (filtrováno)</span>
                ) : (
                  ""
                )}
              </p>
              <div className="flex items-center space-x-1">
                <span className="text-xs text-gray-600">Zobrazit:</span>
                <select
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

      {/* Detail Modal */}
      {selectedOrderId && (
        <PurchaseOrderDetail
          orderId={selectedOrderId}
          isOpen={isDetailModalOpen}
          onClose={handleCloseDetail}
          onEdit={handleEditOrder}
        />
      )}

      {/* Create Modal */}
      <PurchaseOrderForm
        isOpen={isCreateModalOpen}
        onClose={handleCloseCreate}
        onSuccess={handleCreateSuccess}
      />

      {/* Edit Modal */}
      {editOrderId && (
        <PurchaseOrderForm
          isOpen={isEditModalOpen}
          onClose={handleCloseEdit}
          onSuccess={handleEditSuccess}
          editOrderId={editOrderId}
        />
      )}
    </div>
  );
};

export default PurchaseOrderList;
