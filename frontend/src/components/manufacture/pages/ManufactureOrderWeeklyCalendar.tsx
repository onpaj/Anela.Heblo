import React, { useState, useMemo, useEffect } from "react";
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
  Plus,
} from "lucide-react";
import {
  useManufactureOrderCalendarQuery,
  useUpdateManufactureOrderSchedule,
  CalendarEventDto,
} from "../../../api/hooks/useManufactureOrders";
import { ManufactureOrderState } from "../../../api/generated/api-client";
import { usePlanningList } from "../../../contexts/PlanningListContext";
import { useNavigate } from "react-router-dom";

interface ManufactureOrderWeeklyCalendarProps {
  onEventClick?: (orderId: number) => void;
  initialDate?: Date;
  onRefreshAvailable?: (refreshFn: () => void) => void; // Callback to expose refresh function
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
  initialDate,
  onRefreshAvailable,
}) => {
  // Planning list functionality
  const { hasItems, items: planningListItems, removeItem } = usePlanningList();
  const navigate = useNavigate();
  const [showQuickPlanningModal, setShowQuickPlanningModal] = useState(false);
  const [selectedPlanningDate, setSelectedPlanningDate] = useState<Date | null>(null);
  
  // Drag & drop state
  const [draggedEvent, setDraggedEvent] = useState<CalendarEventDto | null>(null);
  const [draggedOverDay, setDraggedOverDay] = useState<string | null>(null);
  
  // Schedule update mutation
  const updateScheduleMutation = useUpdateManufactureOrderSchedule();

  const [currentWeekStart, setCurrentWeekStart] = useState(() => {
    // Use initialDate if provided, otherwise use today
    const targetDate = initialDate || new Date();
    const dayOfWeek = targetDate.getDay();
    const mondayOffset = dayOfWeek === 0 ? -6 : 1 - dayOfWeek; // Handle Sunday (0) as 7
    const monday = new Date(targetDate);
    monday.setDate(targetDate.getDate() + mondayOffset);
    monday.setHours(0, 0, 0, 0);
    return monday;
  });


  // Update currentWeekStart when initialDate changes
  React.useEffect(() => {
    if (initialDate) {
      const dayOfWeek = initialDate.getDay();
      const mondayOffset = dayOfWeek === 0 ? -6 : 1 - dayOfWeek;
      const monday = new Date(initialDate);
      monday.setDate(initialDate.getDate() + mondayOffset);
      monday.setHours(0, 0, 0, 0);
      setCurrentWeekStart(monday);
    }
  }, [initialDate]);

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
    refetch,
  } = useManufactureOrderCalendarQuery(startDate, endDate);

  // Expose refetch function to parent component
  useEffect(() => {
    if (onRefreshAvailable) {
      onRefreshAvailable(refetch);
    }
  }, [onRefreshAvailable, refetch]);

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
      month,
      // Compact format for single line display
      compact: `${dayName.charAt(0).toUpperCase() + dayName.slice(1)} ${dayNumber}. ${month}`
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

  // Quick planning functionality
  const handleQuickPlanClick = (date: Date) => {
    setSelectedPlanningDate(date);
    setShowQuickPlanningModal(true);
  };

  const handlePlanningItemClick = (item: { productCode: string; productName: string }) => {
    // Navigate to batch planning with pre-filled data
    // Send full productCode as backend can now handle full product codes to find semiproducts
    const searchParams = new URLSearchParams({
      productCode: item.productCode,
      productName: item.productCode, // Use full productCode for combobox search
    });

    if (selectedPlanningDate) {
      searchParams.set('date', selectedPlanningDate.toISOString());
    }

    // Remove item from planning list
    removeItem(item.productCode);
    
    // Close modal and navigate
    setShowQuickPlanningModal(false);
    navigate(`/manufacturing/batch-planning?${searchParams.toString()}`);
  };

  // Drag & drop handlers
  const handleDragStart = (event: React.DragEvent, calendarEvent: CalendarEventDto) => {
    setDraggedEvent(calendarEvent);
    event.dataTransfer.effectAllowed = 'move';
    event.dataTransfer.setData('text/plain', JSON.stringify(calendarEvent));
    
    // Add visual feedback
    if (event.currentTarget instanceof HTMLElement) {
      event.currentTarget.style.opacity = '0.5';
    }
  };

  const handleDragEnd = (event: React.DragEvent) => {
    setDraggedEvent(null);
    setDraggedOverDay(null);
    
    // Reset visual feedback
    if (event.currentTarget instanceof HTMLElement) {
      event.currentTarget.style.opacity = '1';
    }
  };

  const handleDragOver = (event: React.DragEvent, dayKey: string) => {
    event.preventDefault();
    event.dataTransfer.dropEffect = 'move';
    setDraggedOverDay(dayKey);
  };

  const handleDragLeave = (event: React.DragEvent) => {
    // Only clear if we're leaving the container, not just moving between children
    if (event.currentTarget === event.target) {
      setDraggedOverDay(null);
    }
  };

  const handleDrop = (event: React.DragEvent, targetDate: Date) => {
    event.preventDefault();
    setDraggedOverDay(null);
    
    if (!draggedEvent || !draggedEvent.id) {
      console.warn('No dragged event or event ID');
      return;
    }

    const originalDate = draggedEvent.date;
    if (!originalDate) {
      console.warn('Dragged event has no date');
      return;
    }

    // Check if we're dropping on the same date
    const targetDateString = targetDate.toISOString().split('T')[0];
    const originalDateString = originalDate.toISOString().split('T')[0];
    
    if (targetDateString === originalDateString) {
      console.log('Dropped on same date, no action needed');
      return;
    }

    // Update both semi-product and product planned dates to the new date
    // This is a simplified approach - in a more complex system you might want to 
    // let the user choose which date to update or maintain the offset between them
    updateScheduleMutation.mutate({
      id: draggedEvent.id,
      semiProductPlannedDate: targetDate,
      productPlannedDate: targetDate,
      changeReason: `Moved from ${originalDate.toLocaleDateString('cs-CZ')} to ${targetDate.toLocaleDateString('cs-CZ')} via drag & drop`
    }, {
      onSuccess: () => {
        console.log('Schedule updated successfully');
      },
      onError: (error) => {
        console.error('Failed to update schedule:', error);
        // You might want to show a toast notification here
      }
    });
  };

  return (
    <div className="h-full flex flex-col bg-gray-50">
      {/* Compact Header */}
      <div className="flex-shrink-0 bg-white rounded-lg shadow-sm border border-gray-200 mb-2">
        <div className="p-2 border-b border-gray-200">
          <div className="flex items-center justify-between">
            <div className="flex items-center space-x-2">
              <Calendar className="h-4 w-4 text-indigo-600" />
              <h2 className="text-sm font-semibold text-gray-900">
                Týdenní kalendář výrobních zakázek
              </h2>
            </div>
            <div className="flex items-center space-x-1">
              {/* Loading indicator for schedule updates */}
              {updateScheduleMutation.isPending && (
                <div className="flex items-center space-x-2 mr-2">
                  <Loader2 className="h-3 w-3 animate-spin text-indigo-500" />
                  <span className="text-xs text-indigo-600">Aktualizace rozpisu...</span>
                </div>
              )}
              <button
                onClick={() => navigateWeek('prev')}
                className="p-1 hover:bg-gray-100 rounded transition-colors"
                title="Předchozí týden"
              >
                <ChevronLeft className="h-3 w-3 text-gray-600" />
              </button>
              <button
                onClick={goToCurrentWeek}
                className="px-2 py-1 text-xs font-medium text-indigo-600 hover:bg-indigo-50 rounded transition-colors"
                title="Přejít na aktuální týden"
              >
                Dnes
              </button>
              <span className="text-sm font-medium text-gray-900 min-w-[160px] text-center">
                {formatWeekRange(startDate, endDate)}
              </span>
              <button
                onClick={() => navigateWeek('next')}
                className="p-1 hover:bg-gray-100 rounded transition-colors"
                title="Následující týden"
              >
                <ChevronRight className="h-3 w-3 text-gray-600" />
              </button>
            </div>
          </div>
        </div>
      </div>

      {/* Calendar Content */}
      <div className="flex-1 bg-white rounded-lg shadow-sm border border-gray-200 overflow-hidden">
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
                      className={`border border-gray-200 rounded-lg h-[600px] flex flex-col ${
                        isToday ? 'border-indigo-300 bg-indigo-50' : 'bg-white'
                      } ${
                        draggedOverDay === dateKey ? 'border-indigo-500 bg-indigo-100' : ''
                      }`}
                      onDragOver={(e) => handleDragOver(e, dateKey)}
                      onDragLeave={handleDragLeave}
                      onDrop={(e) => handleDrop(e, day)}
                    >
                      {/* Day Header - Keep compact but readable */}
                      <div className={`p-2 border-b border-gray-200 ${
                        isToday ? 'bg-indigo-100' : 'bg-gray-50'
                      }`}>
                        <div className="flex items-center justify-between">
                          <div className="flex-1">
                            <div className={`text-sm font-medium text-center ${
                              isToday ? 'text-indigo-700' : 'text-gray-700'
                            }`}>
                              {dayInfo.compact}
                            </div>
                          </div>
                          {/* Quick planning button - only show when there are items */}
                          {hasItems && (
                            <button
                              onClick={() => handleQuickPlanClick(day)}
                              className="ml-1 p-1.5 bg-emerald-500 hover:bg-emerald-600 text-white rounded-md shadow-sm transition-colors"
                              title="Rychlé plánování ze seznamu"
                            >
                              <Plus className="h-3.5 w-3.5" />
                            </button>
                          )}
                        </div>
                      </div>

                      {/* Events - Original style but smaller */}
                      <div className="p-2 space-y-2 flex-1 overflow-y-auto">
                        {dayEvents.map((event, eventIndex) => (
                          <div
                            key={eventIndex}
                            draggable={true}
                            onDragStart={(e) => handleDragStart(e, event)}
                            onDragEnd={handleDragEnd}
                            onClick={() => handleEventClick(event)}
                            className={`
                              p-3 rounded-lg cursor-move transition-all border min-h-[200px] flex flex-col
                              ${event.state ? stateColors[event.state] : 'bg-gray-100 text-gray-800 border-gray-200'}
                              hover:shadow-md hover:scale-[1.02] transform
                              ${draggedEvent?.id === event.id ? 'opacity-50 scale-95' : ''}
                            `}
                            title={`Přetáhněte pro změnu data nebo klikněte pro detail zakázky ${event.orderNumber}${event.manualActionRequired ? `\n⚠️ Vyžaduje ruční zásah` : ''}`}
                          >
                            {/* Order Header */}
                            <div className="flex items-center justify-between mb-2">
                              <div className="flex items-center space-x-1 flex-1 min-w-0">
                                <Factory className="h-4 w-4 flex-shrink-0" />
                                <div className="min-w-0 flex-1">
                                  <div className="text-sm font-bold truncate flex items-center space-x-2">
                                    <span>{event.semiProduct?.productName || event.orderNumber}</span>
                                    {event.manualActionRequired && (
                                      <div 
                                        className="w-2 h-2 bg-red-500 rounded-full flex-shrink-0" 
                                        title="Vyžaduje ruční zásah"
                                      />
                                    )}
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
                                      {(event.semiProduct.actualQuantity ?? event.semiProduct.plannedQuantity)?.toFixed(2)} g
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
                                        <span>{(product.actualQuantity ?? product.plannedQuantity)?.toFixed(2)} ks</span>
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
                          <div className={`text-center text-sm py-8 transition-colors ${
                            draggedOverDay === dateKey 
                              ? 'text-indigo-600 font-medium' 
                              : 'text-gray-400'
                          }`}>
                            {draggedOverDay === dateKey ? 'Pusťte zde pro změnu data' : 'Žádné zakázky'}
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
      </div>

      {/* Legend */}
      <div className="flex-shrink-0 bg-white rounded-lg shadow-sm border border-gray-200 mt-2">
        <div className="p-2">
          <div className="flex flex-wrap gap-3 text-xs">
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

      {/* Quick Planning Modal */}
      {showQuickPlanningModal && selectedPlanningDate && (
        <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
          <div className="bg-white rounded-lg shadow-xl max-w-md w-full mx-4">
            {/* Header */}
            <div className="flex items-center justify-between p-4 border-b border-gray-200">
              <h3 className="text-lg font-semibold text-gray-900">
                Rychlé plánování
              </h3>
              <button
                onClick={() => setShowQuickPlanningModal(false)}
                className="text-gray-400 hover:text-gray-600"
              >
                ×
              </button>
            </div>

            {/* Content */}
            <div className="p-4">
              <p className="text-sm text-gray-600 mb-4">
                Datum: {selectedPlanningDate.toLocaleDateString('cs-CZ')}
              </p>
              
              <div className="space-y-2 max-h-60 overflow-y-auto">
                {planningListItems.map((item) => (
                  <button
                    key={item.productCode}
                    onClick={() => handlePlanningItemClick(item)}
                    className="w-full text-left p-3 rounded border border-gray-200 hover:bg-gray-50 transition-colors"
                  >
                    <div className="font-medium text-gray-900">{item.productName}</div>
                    <div className="text-sm text-gray-500">{item.productCode}</div>
                  </button>
                ))}
              </div>
            </div>

            {/* Footer */}
            <div className="p-4 bg-gray-50 rounded-b-lg">
              <button
                onClick={() => setShowQuickPlanningModal(false)}
                className="w-full bg-gray-200 hover:bg-gray-300 text-gray-800 py-2 px-4 rounded transition-colors"
              >
                Zrušit
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default ManufactureOrderWeeklyCalendar;