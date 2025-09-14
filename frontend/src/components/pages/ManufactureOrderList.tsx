import React, { useState } from "react";
import { useTranslation } from "react-i18next";
import {
  Search,
  Filter,
  AlertCircle,
  Loader2,
  Plus,
  Calendar,
  User,
  Clock,
} from "lucide-react";
import {
  useManufactureOrdersQuery,
  GetManufactureOrdersRequest,
  ManufactureOrderState,
} from "../../api/hooks/useManufactureOrders";
import { PAGE_CONTAINER_HEIGHT } from "../../constants/layout";
import CatalogAutocomplete from "../common/CatalogAutocomplete";
import ManufactureOrderDetail from "./ManufactureOrderDetail";


const stateColors: Record<ManufactureOrderState, string> = {
  [ManufactureOrderState.Draft]: "bg-gray-100 text-gray-800",
  [ManufactureOrderState.SemiProductPlanned]: "bg-blue-100 text-blue-800",
  [ManufactureOrderState.SemiProductManufacture]: "bg-yellow-100 text-yellow-800",
  [ManufactureOrderState.ProductsPlanned]: "bg-indigo-100 text-indigo-800",
  [ManufactureOrderState.ProductsManufacture]: "bg-orange-100 text-orange-800",
  [ManufactureOrderState.Completed]: "bg-green-100 text-green-800",
  [ManufactureOrderState.Cancelled]: "bg-red-100 text-red-800",
};

const ManufactureOrderList: React.FC = () => {
  const { t } = useTranslation();
  
  // Helper function to get translated state label
  const getStateLabel = (state: ManufactureOrderState): string => {
    return t(`manufacture.states.${ManufactureOrderState[state]}`);
  };
  // Filter states - separate input values from applied filters
  const [orderNumberInput, setOrderNumberInput] = useState("");
  const [stateInput, setStateInput] = useState<ManufactureOrderState | "">("");
  const [fromDateInput, setFromDateInput] = useState("");
  const [toDateInput, setToDateInput] = useState("");
  const [responsiblePersonInput, setResponsiblePersonInput] = useState("");
  const [productCodeInput, setProductCodeInput] = useState("");

  const [orderNumberFilter, setOrderNumberFilter] = useState("");
  const [stateFilter, setStateFilter] = useState<ManufactureOrderState | null>(null);
  const [fromDateFilter, setFromDateFilter] = useState("");
  const [toDateFilter, setToDateFilter] = useState("");
  const [responsiblePersonFilter, setResponsiblePersonFilter] = useState("");
  const [productCodeFilter, setProductCodeFilter] = useState("");

  // Modal states
  const [selectedOrderId, setSelectedOrderId] = useState<number | null>(null);
  const [isDetailModalOpen, setIsDetailModalOpen] = useState(false);

  // Build request object
  const request: GetManufactureOrdersRequest = {
    orderNumber: orderNumberFilter || null,
    state: stateFilter,
    dateFrom: fromDateFilter ? new Date(fromDateFilter) : null,
    dateTo: toDateFilter ? new Date(toDateFilter) : null,
    responsiblePerson: responsiblePersonFilter || null,
    productCode: productCodeFilter || null,
  };

  // Use the API query
  const {
    data,
    isLoading: loading,
    error,
    refetch,
  } = useManufactureOrdersQuery(request);

  const orders = data?.orders || [];

  // Handler for applying filters on Enter or button click
  const handleApplyFilters = async () => {
    setOrderNumberFilter(orderNumberInput);
    setStateFilter(stateInput === "" ? null : (stateInput as ManufactureOrderState));
    setFromDateFilter(fromDateInput);
    setToDateFilter(toDateInput);
    setResponsiblePersonFilter(responsiblePersonInput);
    setProductCodeFilter(productCodeInput);

    // Force data reload by refetching
    await refetch();
  };

  // Reset filters
  const handleResetFilters = () => {
    setOrderNumberInput("");
    setStateInput("");
    setFromDateInput("");
    setToDateInput("");
    setResponsiblePersonInput("");
    setProductCodeInput("");
    
    setOrderNumberFilter("");
    setStateFilter(null);
    setFromDateFilter("");
    setToDateFilter("");
    setResponsiblePersonFilter("");
    setProductCodeFilter("");
  };

  // Handle order click to open detail modal
  const handleOrderClick = (orderId: number) => {
    setSelectedOrderId(orderId);
    setIsDetailModalOpen(true);
  };

  // Handle create order - redirect to batch calculator
  const handleCreateOrder = () => {
    // Orders are created only through batch calculation
    window.location.href = '/manufacture/batch-calculator';
  };

  // Handle Enter key for filters
  const handleKeyPress = (event: React.KeyboardEvent) => {
    if (event.key === "Enter") {
      handleApplyFilters();
    }
  };

  // Handle product code selection from autocomplete
  const handleProductCodeSelect = (productCode: string | null) => {
    setProductCodeInput(productCode || "");
  };


  // Format datetime for display
  const formatDateTime = (date: Date | string | undefined) => {
    if (!date) return "-";
    const dateObj = typeof date === "string" ? new Date(date) : date;
    return dateObj.toLocaleString("cs-CZ");
  };

  if (loading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="flex items-center space-x-2">
          <Loader2 className="h-5 w-5 animate-spin text-indigo-500" />
          <div className="text-gray-500">Načítání výrobních zakázek...</div>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="flex items-center space-x-2 text-red-600">
          <AlertCircle className="h-5 w-5" />
          <div>Chyba při načítání výrobních zakázek: {error.message}</div>
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
          Výrobní zakázky
        </h1>
        <button
          onClick={handleCreateOrder}
          className="bg-indigo-600 hover:bg-indigo-700 text-white font-medium py-2 px-4 rounded-md transition-colors duration-200 text-sm flex items-center gap-2"
          title="Zakázky se vytváří prostřednictvím batch kalkulátoru"
        >
          <Plus className="h-4 w-4" />
          Nová zakázka
        </button>
      </div>

      {/* Filters - Fixed */}
      <div className="flex-shrink-0 bg-white shadow rounded-lg p-4 mb-4">
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-6 gap-3">
          <div className="flex items-center">
            <Filter className="h-4 w-4 text-gray-400 mr-2" />
            <span className="text-sm font-medium text-gray-900">Filtry:</span>
          </div>

          {/* Order Number */}
          <div className="flex-1">
            <div className="relative">
              <div className="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none">
                <Search className="h-4 w-4 text-gray-400" />
              </div>
              <input
                type="text"
                placeholder="Číslo zakázky"
                value={orderNumberInput}
                onChange={(e) => setOrderNumberInput(e.target.value)}
                onKeyPress={handleKeyPress}
                className="block w-full pl-10 pr-3 py-2 border border-gray-300 rounded-md leading-5 bg-white placeholder-gray-500 focus:outline-none focus:placeholder-gray-400 focus:ring-1 focus:ring-indigo-500 focus:border-indigo-500 text-sm"
              />
            </div>
          </div>

          {/* State */}
          <div className="flex-1">
            <select
              value={stateInput}
              onChange={(e) => setStateInput(e.target.value as ManufactureOrderState | "")}
              className="block w-full pl-3 pr-10 py-2 text-sm border border-gray-300 bg-white rounded-md focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-indigo-500"
            >
              <option value="">Všechny stavy</option>
              {Object.values(ManufactureOrderState)
                .filter(value => typeof value === 'number')
                .map((state) => (
                <option key={state} value={state}>
                  {getStateLabel(state as ManufactureOrderState)}
                </option>
              ))}
            </select>
          </div>

          {/* Date From */}
          <div className="flex-1">
            <div className="relative">
              <div className="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none">
                <Calendar className="h-4 w-4 text-gray-400" />
              </div>
              <input
                type="date"
                placeholder="Od data"
                value={fromDateInput}
                onChange={(e) => setFromDateInput(e.target.value)}
                className="block w-full pl-10 pr-3 py-2 border border-gray-300 rounded-md leading-5 bg-white placeholder-gray-500 focus:outline-none focus:placeholder-gray-400 focus:ring-1 focus:ring-indigo-500 focus:border-indigo-500 text-sm"
              />
            </div>
          </div>

          {/* Date To */}
          <div className="flex-1">
            <div className="relative">
              <div className="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none">
                <Calendar className="h-4 w-4 text-gray-400" />
              </div>
              <input
                type="date"
                placeholder="Do data"
                value={toDateInput}
                onChange={(e) => setToDateInput(e.target.value)}
                className="block w-full pl-10 pr-3 py-2 border border-gray-300 rounded-md leading-5 bg-white placeholder-gray-500 focus:outline-none focus:placeholder-gray-400 focus:ring-1 focus:ring-indigo-500 focus:border-indigo-500 text-sm"
              />
            </div>
          </div>

          {/* Responsible Person */}
          <div className="flex-1">
            <div className="relative">
              <div className="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none">
                <User className="h-4 w-4 text-gray-400" />
              </div>
              <input
                type="text"
                placeholder="Odpovědná osoba"
                value={responsiblePersonInput}
                onChange={(e) => setResponsiblePersonInput(e.target.value)}
                onKeyPress={handleKeyPress}
                className="block w-full pl-10 pr-3 py-2 border border-gray-300 rounded-md leading-5 bg-white placeholder-gray-500 focus:outline-none focus:placeholder-gray-400 focus:ring-1 focus:ring-indigo-500 focus:border-indigo-500 text-sm"
              />
            </div>
          </div>
        </div>

        {/* Product Code Filter - Full width on second row */}
        <div className="mt-3">
          <div className="flex-1">
            <CatalogAutocomplete<string>
              value={productCodeInput}
              onSelect={handleProductCodeSelect}
              placeholder="Kód produktu"
              className="w-full"
              allowManualEntry={true}
              itemAdapter={(item) => item.productCode || ""}
            />
          </div>
        </div>

        {/* Filter buttons */}
        <div className="mt-3 flex items-center gap-2">
          <button
            onClick={handleApplyFilters}
            className="bg-indigo-600 hover:bg-indigo-700 text-white font-medium py-2 px-4 rounded-md transition-colors duration-200 text-sm flex items-center gap-2"
          >
            <Filter className="h-4 w-4" />
            Použít filtry
          </button>
          <button
            onClick={handleResetFilters}
            className="bg-gray-600 hover:bg-gray-700 text-white font-medium py-2 px-4 rounded-md transition-colors duration-200 text-sm"
          >
            Vymazat filtry
          </button>
        </div>
      </div>

      {/* Table - Scrollable */}
      <div className="flex-1 bg-white shadow rounded-lg overflow-hidden">
        <div className="overflow-x-auto">
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-gray-50 sticky top-0 z-10">
              <tr>
                <th
                  scope="col"
                  className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider"
                >
                  Číslo zakázky
                </th>
                <th
                  scope="col"
                  className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider"
                >
                  Stav
                </th>
                <th
                  scope="col"
                  className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider"
                >
                  Datum vytvoření
                </th>
                <th
                  scope="col"
                  className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider"
                >
                  Odpovědná osoba
                </th>
                <th
                  scope="col"
                  className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider"
                >
                  Produkt
                </th>
                <th
                  scope="col"
                  className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider"
                >
                  Variant
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
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                    {order.state !== undefined && (
                      <span
                        className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${stateColors[order.state]}`}
                      >
                        {getStateLabel(order.state)}
                      </span>
                    )}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                    {formatDateTime(order.createdDate)}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                    {order.responsiblePerson || "-"}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                    {order.semiProduct?.productName} ({order.semiProduct?.productCode})
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500 text-center">
                    {order.products?.length || 0}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>

          {orders.length === 0 && (
            <div className="text-center py-8">
              <Clock className="mx-auto h-12 w-12 text-gray-300" />
              <p className="mt-2 text-gray-500">Žádné výrobní zakázky nebyly nalezeny.</p>
            </div>
          )}
        </div>
      </div>

      {/* ManufactureOrderDetail Modal */}
      {selectedOrderId && (
        <ManufactureOrderDetail
          orderId={selectedOrderId}
          isOpen={isDetailModalOpen}
          onClose={() => {
            setIsDetailModalOpen(false);
            setSelectedOrderId(null);
          }}
        />
      )}

      {/* TODO: Add CreateManufactureOrder modal when implemented */}
    </div>
  );
};

export default ManufactureOrderList;