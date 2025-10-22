import React, { useState } from "react";
import {
  Calendar,
  Clock,
  Grid,
  CalendarDays,
} from "lucide-react";
import { useSearchParams } from "react-router-dom";
import {
  useManufactureOrdersQuery,
  GetManufactureOrdersRequest,
} from "../../../api/hooks/useManufactureOrders";
import { ManufactureOrderDto, ManufactureOrderState } from "../../../api/generated/api-client";
import { PAGE_CONTAINER_HEIGHT } from "../../../constants/layout";
import LoadingState from "../../common/LoadingState";
import ErrorState from "../../common/ErrorState";
import ManufactureOrderStateChip from "../shared/ManufactureOrderStateChip";
import ManufactureOrderFilters from "../list/ManufactureOrderFilters";
import ManufactureOrderDetail from "./ManufactureOrderDetail";
import ManufactureOrderCalendar from "./ManufactureOrderCalendar";
import ManufactureOrderWeeklyCalendar from "./ManufactureOrderWeeklyCalendar";

const ManufactureOrderList: React.FC = () => {
  const [searchParams] = useSearchParams();
  
  // Filter state - managed by the filters component, initialize from URL params
  const [filters, setFilters] = useState<GetManufactureOrdersRequest>(() => {
    const manualActionRequiredParam = searchParams.get('manualActionRequired');
    const stateParam = searchParams.get('state');
    const dateFromParam = searchParams.get('dateFrom');
    const dateToParam = searchParams.get('dateTo');
    
    // Convert string state parameter to enum value
    const stateEnum = stateParam && Object.values(ManufactureOrderState).includes(stateParam as ManufactureOrderState) 
      ? stateParam as ManufactureOrderState 
      : null;
    
    // Convert string date parameters to Date objects
    const dateFromValue = dateFromParam ? new Date(dateFromParam) : null;
    const dateToValue = dateToParam ? new Date(dateToParam) : null;
    
    return {
      orderNumber: searchParams.get('orderNumber') || null,
      state: stateEnum,
      dateFrom: dateFromValue,
      dateTo: dateToValue,
      responsiblePerson: searchParams.get('responsiblePerson') || null,
      productCode: searchParams.get('productCode') || null,
      erpDocumentNumber: searchParams.get('erpDocumentNumber') || null,
      manualActionRequired: manualActionRequiredParam === 'true' ? true : manualActionRequiredParam === 'false' ? false : null,
    };
  });

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

  // Use the API query
  const {
    data,
    isLoading: loading,
    error,
    refetch,
  } = useManufactureOrdersQuery(filters);

  const orders: ManufactureOrderDto[] = data?.orders || [];

  // Handle order click to open detail modal
  const handleOrderClick = (orderId: number) => {
    setSelectedOrderId(orderId);
    setIsDetailModalOpen(true);
  };

  // Handle calendar event click (open order detail)
  const handleCalendarEventClick = (orderId: number) => {
    setSelectedOrderId(orderId);
    setIsDetailModalOpen(true);
  };

  // Format date only (without time) for display
  const formatDateOnly = (date: Date | string | undefined) => {
    if (!date) return "-";
    const dateObj = typeof date === "string" ? new Date(date) : date;
    return dateObj.toLocaleDateString("cs-CZ");
  };

  if (loading) {
    return <LoadingState message="Načítání výrobních zakázek..." />;
  }

  if (error) {
    return <ErrorState message={`Chyba při načítání výrobních zakázek: ${error.message}`} />;
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

      {/* Filters */}
      <ManufactureOrderFilters
        onFiltersChange={setFilters}
        onApplyFilters={async () => {
          await refetch();
        }}
      />

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
                  Datum výroby
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
                  ERP výdejka přebytků
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
                      <ManufactureOrderStateChip state={order.state} />
                    )}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                    {formatDateOnly(order.plannedDate)}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                    {order.erpOrderNumberSemiproduct || "-"}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                    {order.erpOrderNumberProduct || "-"}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                    {order.erpDiscardResidueDocumentNumber || "-"}
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