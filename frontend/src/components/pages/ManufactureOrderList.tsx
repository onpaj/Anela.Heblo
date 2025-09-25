import React, { useState } from "react";
import { useTranslation } from "react-i18next";
import {
  Search,
  Filter,
  AlertCircle,
  Loader2,
  Calendar,
  Clock,
  Grid,
  CalendarDays,
  ChevronDown,
} from "lucide-react";
import { useSearchParams } from "react-router-dom";
import {
  useManufactureOrdersQuery,
  GetManufactureOrdersRequest,
  ManufactureOrderState,
} from "../../api/hooks/useManufactureOrders";
import { ProductType } from "../../api/generated/api-client";
import { PAGE_CONTAINER_HEIGHT } from "../../constants/layout";
import CatalogAutocomplete from "../common/CatalogAutocomplete";
import ResponsiblePersonCombobox from "../common/ResponsiblePersonCombobox";
import ManufactureOrderDetail from "./ManufactureOrderDetail";
import ManufactureOrderCalendar from "./ManufactureOrderCalendar";
import ManufactureOrderWeeklyCalendar from "./ManufactureOrderWeeklyCalendar";


const stateColors: Record<ManufactureOrderState, string> = {
  [ManufactureOrderState.Draft]: "bg-gray-100 text-gray-800",
  [ManufactureOrderState.Planned]: "bg-blue-100 text-blue-800",
  [ManufactureOrderState.SemiProductManufactured]: "bg-yellow-100 text-yellow-800",
  [ManufactureOrderState.Completed]: "bg-green-100 text-green-800",
  [ManufactureOrderState.Cancelled]: "bg-red-100 text-red-800",
};

const ManufactureOrderList: React.FC = () => {
  const { t } = useTranslation();
  const [searchParams] = useSearchParams();
  
  // Helper function to get translated state label
  const getStateLabel = (state: ManufactureOrderState): string => {
    return t(`manufacture.states.${ManufactureOrderState[state]}`);
  };
  // State for collapsible filter section
  const [isFiltersCollapsed, setIsFiltersCollapsed] = useState(true);

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

  // View mode state - initialize from URL params
  const [viewMode, setViewMode] = useState<'grid' | 'calendar' | 'weekly'>(() => {
    const view = searchParams.get('view');
    return (view === 'weekly' || view === 'calendar' || view === 'grid') ? view : 'weekly';
  });

  // Initial date for weekly calendar from URL params
  const initialCalendarDate = React.useMemo(() => {
    const dateParam = searchParams.get('date');
    return dateParam ? new Date(dateParam) : undefined;
  }, [searchParams]);

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

  // Handle calendar event click (open order detail)
  const handleCalendarEventClick = (orderId: number) => {
    setSelectedOrderId(orderId);
    setIsDetailModalOpen(true);
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
        <div className="flex items-center space-x-4">
          <h1 className="text-lg font-semibold text-gray-900">
            Výrobní zakázky
          </h1>
          
          {/* View Toggle */}
          <div className="flex rounded-lg border border-gray-300 p-1">
            <button
              onClick={() => setViewMode('weekly')}
              className={`flex items-center px-3 py-1.5 text-sm font-medium rounded-md transition-colors ${
                viewMode === 'weekly'
                  ? 'bg-white text-gray-900 shadow-sm border border-gray-200'
                  : 'text-gray-500 hover:text-gray-700'
              }`}
              title="Zobrazit jako týdenní kalendář"
            >
              <Calendar className="h-4 w-4 mr-1.5" />
              Týden
            </button>
            <button
              onClick={() => setViewMode('calendar')}
              className={`flex items-center px-3 py-1.5 text-sm font-medium rounded-md transition-colors ${
                viewMode === 'calendar'
                  ? 'bg-white text-gray-900 shadow-sm border border-gray-200'
                  : 'text-gray-500 hover:text-gray-700'
              }`}
              title="Zobrazit jako měsíční kalendář"
            >
              <CalendarDays className="h-4 w-4 mr-1.5" />
              Měsíc
            </button>
            <button
              onClick={() => setViewMode('grid')}
              className={`flex items-center px-3 py-1.5 text-sm font-medium rounded-md transition-colors ${
                viewMode === 'grid'
                  ? 'bg-white text-gray-900 shadow-sm border border-gray-200'
                  : 'text-gray-500 hover:text-gray-700'
              }`}
              title="Zobrazit jako tabulka"
            >
              <Grid className="h-4 w-4 mr-1.5" />
              Tabulka
            </button>
          </div>
        </div>
      </div>

      {/* Compact Collapsible Filters */}
      <div className="flex-shrink-0 bg-white shadow rounded-lg mb-4">
        <div className="p-3 border-b border-gray-200">
          <div className="flex items-center justify-between">
            <button
              onClick={() => setIsFiltersCollapsed(!isFiltersCollapsed)}
              className="flex items-center space-x-2 text-sm font-medium text-gray-900 hover:text-gray-700"
            >
              <ChevronDown
                className={`h-4 w-4 transition-transform ${
                  isFiltersCollapsed ? "-rotate-90" : ""
                }`}
              />
              <Filter className="h-4 w-4" />
              <span>Filtry</span>
            </button>
            
            {/* Quick summary when collapsed */}
            {isFiltersCollapsed && (
              <div className="flex items-center space-x-3 text-xs">
                {/* Quick filter info */}
                {(orderNumberFilter || stateFilter || responsiblePersonFilter || productCodeFilter) ? (
                  <span className="text-gray-600">Aktivní filtry</span>
                ) : (
                  <span className="text-gray-500">Klikněte pro rozbalení filtrů</span>
                )}
                
                {/* Quick apply button when collapsed */}
                <div className="flex items-center space-x-2">
                  <CatalogAutocomplete<string>
                    value={productCodeInput}
                    onSelect={handleProductCodeSelect}
                    placeholder="Produkt..."
                    className="w-48 text-xs"
                    allowManualEntry={true}
                    productTypes={[ProductType.Product, ProductType.SemiProduct]}
                    itemAdapter={(item) => item.productCode || ""}
                    size="sm"
                  />
                  <button
                    onClick={handleApplyFilters}
                    className="px-6 py-1 bg-indigo-600 text-white rounded text-xs hover:bg-indigo-700 whitespace-nowrap"
                  >
                    Hledat produkt
                  </button>
                </div>
              </div>
            )}
          </div>
        </div>

        {!isFiltersCollapsed && (
          <div className="p-3 bg-gray-50">
            <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-3 text-xs">
              {/* Order Number */}
              <div>
                <label className="block text-xs font-medium text-gray-700 mb-1">
                  Číslo zakázky
                </label>
                <div className="relative">
                  <Search className="absolute left-2 top-1/2 transform -translate-y-1/2 h-3 w-3 text-gray-400" />
                  <input
                    type="text"
                    placeholder="Číslo zakázky"
                    value={orderNumberInput}
                    onChange={(e) => setOrderNumberInput(e.target.value)}
                    onKeyDown={handleKeyPress}
                    className="pl-7 w-full border border-gray-300 rounded px-2 py-1.5 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-transparent"
                  />
                </div>
              </div>

              {/* State */}
              <div>
                <label className="block text-xs font-medium text-gray-700 mb-1">
                  Stav zakázky
                </label>
                <select
                  value={stateInput}
                  onChange={(e) => setStateInput(e.target.value as ManufactureOrderState | "")}
                  className="w-full border border-gray-300 rounded px-2 py-1.5 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-transparent"
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
              <div>
                <label className="block text-xs font-medium text-gray-700 mb-1">
                  Od data
                </label>
                <input
                  type="date"
                  value={fromDateInput}
                  onChange={(e) => setFromDateInput(e.target.value)}
                  className="w-full border border-gray-300 rounded px-2 py-1.5 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-transparent"
                />
              </div>

              {/* Date To */}
              <div>
                <label className="block text-xs font-medium text-gray-700 mb-1">
                  Do data
                </label>
                <input
                  type="date"
                  value={toDateInput}
                  onChange={(e) => setToDateInput(e.target.value)}
                  className="w-full border border-gray-300 rounded px-2 py-1.5 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-transparent"
                />
              </div>

              {/* Responsible Person */}
              <div>
                <label className="block text-xs font-medium text-gray-700 mb-1">
                  Odpovědná osoba
                </label>
                <ResponsiblePersonCombobox
                  value={responsiblePersonInput}
                  onChange={(value) => setResponsiblePersonInput(value || "")}
                  placeholder="Odpovědná osoba"
                  allowManualEntry={true}
                  className="w-full text-xs"
                />
              </div>

              {/* Product Code */}
              <div>
                <label className="block text-xs font-medium text-gray-700 mb-1">
                  Kód produktu
                </label>
                <CatalogAutocomplete<string>
                  value={productCodeInput}
                  onSelect={handleProductCodeSelect}
                  placeholder="Kód produktu"
                  className="w-full text-xs"
                  allowManualEntry={true}
                  productTypes={[ProductType.Product, ProductType.SemiProduct]}
                  itemAdapter={(item) => item.productCode || ""}
                />
              </div>
            </div>

            {/* Filter buttons */}
            <div className="mt-3 flex items-center gap-2">
              <button
                onClick={handleApplyFilters}
                className="bg-indigo-600 hover:bg-indigo-700 text-white font-medium py-1.5 px-3 rounded text-xs transition-colors duration-200 flex items-center gap-1"
              >
                <Filter className="h-3 w-3" />
                Použít filtry
              </button>
              <button
                onClick={handleResetFilters}
                className="bg-gray-600 hover:bg-gray-700 text-white font-medium py-1.5 px-3 rounded text-xs transition-colors duration-200"
              >
                Vymazat filtry
              </button>
            </div>
          </div>
        )}
      </div>

      {/* Content - Grid, Monthly Calendar, or Weekly Calendar */}
      <div className="flex-1">
        {viewMode === 'calendar' ? (
          <ManufactureOrderCalendar onEventClick={handleCalendarEventClick} />
        ) : viewMode === 'weekly' ? (
          <ManufactureOrderWeeklyCalendar 
            onEventClick={handleCalendarEventClick} 
            initialDate={initialCalendarDate}
          />
        ) : (
          <div className="bg-white shadow rounded-lg overflow-hidden">
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
                  ERP č. (meziprod.)
                </th>
                <th
                  scope="col"
                  className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider"
                >
                  ERP č. (produkt)
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
                    <div className="flex items-center space-x-2">
                      <span>{order.orderNumber}</span>
                      {order.manualActionRequired && (
                        <div 
                          className="w-2 h-2 bg-red-500 rounded-full flex-shrink-0" 
                          title="Vyžaduje ruční zásah"
                        />
                      )}
                    </div>
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
                    {order.erpOrderNumberSemiproduct || "-"}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                    {order.erpOrderNumberProduct || "-"}
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
        )}
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