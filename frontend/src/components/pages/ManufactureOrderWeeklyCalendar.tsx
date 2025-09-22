import React, { useState, useMemo } from "react";
import { 
  ChevronLeft, 
  ChevronRight,
  Calendar,
  Factory,
  User,
  Loader2,
  AlertCircle,
  Package,
  Hash,
  Layers,
} from "lucide-react";
import {
  useManufactureOrderCalendarQuery,
  CalendarEventDto,
  ManufactureOrderState,
} from "../../api/hooks/useManufactureOrders";

interface ManufactureOrderWeeklyCalendarProps {
  onEventClick?: (orderId: number) => void;
}

const stateColors: Record<ManufactureOrderState, string> = {
  [ManufactureOrderState.Draft]: "bg-gray-100 text-gray-800 border-gray-200",
  [ManufactureOrderState.Planned]: "bg-blue-100 text-blue-800 border-blue-200",
  [ManufactureOrderState.SemiProductManufactured]: "bg-yellow-100 text-yellow-800 border-yellow-200",
  [ManufactureOrderState.Completed]: "bg-green-100 text-green-800 border-green-200",
  [ManufactureOrderState.Cancelled]: "bg-red-100 text-red-800 border-red-200",
};


const ManufactureOrderWeeklyCalendar: React.FC<ManufactureOrderWeeklyCalendarProps> = ({
  onEventClick,
}) => {
  const [currentWeekStart, setCurrentWeekStart] = useState(() => {
    // Get current week start (Monday)
    const today = new Date();
    const dayOfWeek = today.getDay();
    const mondayOffset = dayOfWeek === 0 ? -6 : 1 - dayOfWeek; // Handle Sunday (0) as 7
    const monday = new Date(today);
    monday.setDate(today.getDate() + mondayOffset);
    monday.setHours(0, 0, 0, 0);
    return monday;
  });

  // Calculate week boundaries
  const { startDate, endDate, weekDays } = useMemo(() => {
    const start = new Date(currentWeekStart);
    const end = new Date(currentWeekStart);
    end.setDate(start.getDate() + 4); // Friday (Monday + 4 days)
    end.setHours(23, 59, 59, 999);

    // Generate weekdays (Monday to Friday)
    const days = [];
    for (let i = 0; i < 5; i++) {
      const day = new Date(start);
      day.setDate(start.getDate() + i);
      days.push(day);
    }

    return {
      startDate: start,
      endDate: end,
      weekDays: days,
    };
  }, [currentWeekStart]);

  // Fetch calendar data
  const {
    data: calendarData,
    isLoading,
    error,
  } = useManufactureOrderCalendarQuery(startDate, endDate);

  // Group events by date
  const eventsByDate = useMemo(() => {
    const grouped: Record<string, CalendarEventDto[]> = {};
    
    if (calendarData?.events) {
      calendarData.events.forEach(event => {
        if (event.date) {
          const dateKey = event.date.toISOString().split('T')[0];
          if (!grouped[dateKey]) {
            grouped[dateKey] = [];
          }
          grouped[dateKey].push(event);
        }
      });
    }
    
    return grouped;
  }, [calendarData]);

  const navigateWeek = (direction: 'prev' | 'next') => {
    setCurrentWeekStart(prev => {
      const newDate = new Date(prev);
      if (direction === 'prev') {
        newDate.setDate(prev.getDate() - 7);
      } else {
        newDate.setDate(prev.getDate() + 7);
      }
      return newDate;
    });
  };

  const formatWeekRange = (start: Date, end: Date) => {
    const startStr = start.toLocaleDateString('cs-CZ', { 
      day: 'numeric',
      month: 'numeric',
      year: 'numeric'
    });
    const endStr = end.toLocaleDateString('cs-CZ', { 
      day: 'numeric',
      month: 'numeric',
      year: 'numeric'
    });
    return `${startStr} - ${endStr}`;
  };

  const formatDayHeader = (date: Date) => {
    const dayName = date.toLocaleDateString('cs-CZ', { weekday: 'short' });
    const dayNumber = date.getDate();
    const month = date.toLocaleDateString('cs-CZ', { month: 'short' });
    return {
      dayName: dayName.charAt(0).toUpperCase() + dayName.slice(1),
      dayNumber,
      month
    };
  };

  const handleEventClick = (event: CalendarEventDto) => {
    if (onEventClick && event.id) {
      onEventClick(event.id);
    }
  };

  const goToCurrentWeek = () => {
    const today = new Date();
    const dayOfWeek = today.getDay();
    const mondayOffset = dayOfWeek === 0 ? -6 : 1 - dayOfWeek;
    const monday = new Date(today);
    monday.setDate(today.getDate() + mondayOffset);
    monday.setHours(0, 0, 0, 0);
    setCurrentWeekStart(monday);
  };

  return (
    <div className="bg-white rounded-lg shadow-sm border border-gray-200 h-full flex flex-col">
      {/* Calendar Header */}
      <div className="flex items-center justify-between p-4 border-b border-gray-200">
        <div className="flex items-center space-x-3">
          <Calendar className="h-5 w-5 text-indigo-600" />
          <h2 className="text-lg font-semibold text-gray-900">
            Týdenní kalendář výrobních zakázek
          </h2>
        </div>
        <div className="flex items-center space-x-2">
          <button
            onClick={() => navigateWeek('prev')}
            className="p-2 hover:bg-gray-100 rounded-lg transition-colors"
            title="Předchozí týden"
          >
            <ChevronLeft className="h-4 w-4 text-gray-600" />
          </button>
          <button
            onClick={goToCurrentWeek}
            className="px-3 py-1.5 text-sm font-medium text-indigo-600 hover:bg-indigo-50 rounded-lg transition-colors"
            title="Přejít na aktuální týden"
          >
            Dnes
          </button>
          <span className="text-lg font-medium text-gray-900 min-w-[200px] text-center">
            {formatWeekRange(startDate, endDate)}
          </span>
          <button
            onClick={() => navigateWeek('next')}
            className="p-2 hover:bg-gray-100 rounded-lg transition-colors"
            title="Následující týden"
          >
            <ChevronRight className="h-4 w-4 text-gray-600" />
          </button>
        </div>
      </div>

      {/* Calendar Content */}
      <div className="p-4 flex-1 overflow-y-auto">
        {isLoading ? (
          <div className="flex items-center justify-center h-64">
            <div className="flex items-center space-x-2">
              <Loader2 className="h-5 w-5 animate-spin text-indigo-500" />
              <div className="text-gray-500">Načítání týdenního kalendáře...</div>
            </div>
          </div>
        ) : error ? (
          <div className="flex items-center justify-center h-64">
            <div className="flex items-center space-x-2 text-red-600">
              <AlertCircle className="h-5 w-5" />
              <div>Chyba při načítání kalendáře</div>
            </div>
          </div>
        ) : (
          <>
            {/* Week Days Header */}
            <div className="grid grid-cols-5 gap-4 mb-4">
              {weekDays.map((day, index) => {
                const dayInfo = formatDayHeader(day);
                const dateKey = day.toISOString().split('T')[0];
                const dayEvents = eventsByDate[dateKey] || [];
                const isToday = day.toDateString() === new Date().toDateString();
                
                return (
                  <div 
                    key={index}
                    className={`border border-gray-200 rounded-lg h-[900px] flex flex-col ${
                      isToday ? 'border-indigo-300 bg-indigo-50' : 'bg-white'
                    }`}
                  >
                    {/* Day Header */}
                    <div className={`p-3 border-b border-gray-200 ${
                      isToday ? 'bg-indigo-100' : 'bg-gray-50'
                    }`}>
                      <div className="text-center">
                        <div className={`text-sm font-medium ${
                          isToday ? 'text-indigo-700' : 'text-gray-600'
                        }`}>
                          {dayInfo.dayName}
                        </div>
                        <div className={`text-lg font-bold ${
                          isToday ? 'text-indigo-900' : 'text-gray-900'
                        }`}>
                          {dayInfo.dayNumber}
                        </div>
                        <div className={`text-xs ${
                          isToday ? 'text-indigo-600' : 'text-gray-500'
                        }`}>
                          {dayInfo.month}
                        </div>
                      </div>
                    </div>

                    {/* Events */}
                    <div className="p-2 space-y-2 flex-1 overflow-y-auto">
                      {dayEvents.map((event, eventIndex) => (
                        <div
                          key={eventIndex}
                          onClick={() => handleEventClick(event)}
                          className={`
                            p-3 rounded-lg cursor-pointer transition-all border min-h-[300px] flex flex-col
                            ${event.state ? stateColors[event.state] : 'bg-gray-100 text-gray-800 border-gray-200'}
                            hover:shadow-md hover:scale-[1.02] transform
                          `}
                          title={`Klikněte pro detail zakázky ${event.orderNumber}`}
                        >
                          {/* Order Header */}
                          <div className="flex items-center justify-between mb-2">
                            <div className="flex items-center space-x-1 flex-1 min-w-0">
                              <Factory className="h-4 w-4 flex-shrink-0" />
                              <div className="min-w-0 flex-1">
                                <div className="text-sm font-bold truncate">
                                  {event.semiProduct?.productName || event.orderNumber}
                                </div>
                                {event.semiProduct?.productCode && (
                                  <div className="text-xs text-gray-600 truncate">
                                    {event.semiProduct.productCode}
                                  </div>
                                )}
                              </div>
                            </div>
                          </div>

                          {/* Semi-product Information - Compact */}
                          {event.semiProduct && (
                            <div className="mb-2 p-1.5 bg-white bg-opacity-50 rounded border">
                              <div className="flex items-center justify-center space-x-2">
                                <div className="flex items-center space-x-1">
                                  <Hash className="h-3 w-3" />
                                  <span className="text-xs font-medium">
                                    {event.semiProduct.plannedQuantity?.toFixed(2)} ks
                                  </span>
                                </div>
                                {event.semiProduct.batchMultiplier && (
                                  <div className="flex items-center space-x-1">
                                    <Layers className="h-3 w-3" />
                                    <span className="text-xs font-medium">
                                      ×{event.semiProduct.batchMultiplier.toFixed(2)}
                                    </span>
                                  </div>
                                )}
                              </div>
                            </div>
                          )}

                          {/* Products List */}
                          {event.products && event.products.length > 0 && (
                            <div className="flex-1 flex flex-col">
                              <div className="text-xs font-medium mb-1 flex items-center space-x-1">
                                <Package className="h-3 w-3" />
                                <span>Produkty ({event.products.length}):</span>
                              </div>
                              <div className="space-y-1 flex-1">
                                {event.products.map((product, idx) => (
                                  <div key={idx} className="text-xs p-2 bg-white bg-opacity-30 rounded">
                                    <div className="font-medium truncate" title={product.productName}>
                                      {product.productName}
                                    </div>
                                    <div className="text-gray-600 flex items-center justify-between">
                                      <span className="truncate">{product.productCode}</span>
                                      <span>{product.plannedQuantity?.toFixed(2)} ks</span>
                                    </div>
                                  </div>
                                ))}
                              </div>
                            </div>
                          )}

                          {/* Responsible Person */}
                          {event.responsiblePerson && (
                            <div className="flex items-center space-x-1 text-xs mt-2 pt-2 border-t border-white border-opacity-30">
                              <User className="h-3 w-3" />
                              <span className="truncate font-medium">
                                {event.responsiblePerson}
                              </span>
                            </div>
                          )}
                        </div>
                      ))}
                      
                      {dayEvents.length === 0 && (
                        <div className="text-center text-gray-400 text-sm py-8">
                          Žádné zakázky
                        </div>
                      )}
                    </div>
                  </div>
                );
              })}
            </div>
          </>
        )}
      </div>

      {/* Legend */}
      <div className="border-t border-gray-200 p-4">
        <div className="flex flex-wrap gap-4 text-xs">
          <div className="flex items-center space-x-4">
            <span className="font-medium text-gray-700">Stavy zakázek:</span>
            {Object.entries(stateColors).slice(0, 4).map(([state, colorClass]) => (
              <div key={state} className="flex items-center space-x-1">
                <div className={`w-3 h-3 rounded ${colorClass.split(' ')[0]} border`}></div>
                <span className="text-gray-600 capitalize">
                  {state}
                </span>
              </div>
            ))}
          </div>
        </div>
      </div>
    </div>
  );
};

export default ManufactureOrderWeeklyCalendar;