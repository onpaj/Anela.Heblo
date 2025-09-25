import React, { useState, useMemo } from "react";
import { 
  ChevronLeft, 
  ChevronRight,
  Calendar,
  Factory,
  User,
  Loader2,
  AlertCircle,
} from "lucide-react";
import {
  useManufactureOrderCalendarQuery,
  CalendarEventDto,
  ManufactureOrderState,
} from "../../api/hooks/useManufactureOrders";

interface ManufactureOrderCalendarProps {
  onEventClick?: (orderId: number) => void;
}

const stateColors: Record<ManufactureOrderState, string> = {
  [ManufactureOrderState.Draft]: "bg-gray-100 text-gray-800 border-gray-200",
  [ManufactureOrderState.Planned]: "bg-blue-100 text-blue-800 border-blue-200",
  [ManufactureOrderState.SemiProductManufactured]: "bg-yellow-100 text-yellow-800 border-yellow-200",
  [ManufactureOrderState.Completed]: "bg-green-100 text-green-800 border-green-200",
  [ManufactureOrderState.Cancelled]: "bg-red-100 text-red-800 border-red-200",
};


const ManufactureOrderCalendar: React.FC<ManufactureOrderCalendarProps> = ({
  onEventClick,
}) => {
  const [currentDate, setCurrentDate] = useState(new Date());

  // Calculate start and end of the month for the query
  const { startDate, endDate } = useMemo(() => {
    const year = currentDate.getFullYear();
    const month = currentDate.getMonth();
    
    const startDate = new Date(year, month, 1);
    const endDate = new Date(year, month + 1, 0); // Last day of the month
    
    return { startDate, endDate };
  }, [currentDate]);

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

  // Generate calendar grid (Monday to Friday only)
  const calendarDays = useMemo(() => {
    const year = currentDate.getFullYear();
    const month = currentDate.getMonth();
    
    const firstDay = new Date(year, month, 1);
    const lastDay = new Date(year, month + 1, 0);
    // Convert Sunday-based (0-6) to Monday-based (0-6), where 0 = Monday
    const firstDayOfWeek = (firstDay.getDay() + 6) % 7; // 0 = Monday, 1 = Tuesday, ..., 6 = Sunday
    const daysInMonth = lastDay.getDate();
    
    const days: Array<{
      date: number;
      isCurrentMonth: boolean;
      dateObj: Date;
      events: CalendarEventDto[];
    }> = [];

    // Add empty cells for days before the first day of the month (Monday to Friday only)
    for (let i = 0; i < firstDayOfWeek; i++) {
      const prevDate = new Date(year, month, -firstDayOfWeek + i + 1);
      // Skip weekends
      if (prevDate.getDay() !== 0 && prevDate.getDay() !== 6) {
        const dateKey = prevDate.toISOString().split('T')[0];
        days.push({
          date: prevDate.getDate(),
          isCurrentMonth: false,
          dateObj: prevDate,
          events: eventsByDate[dateKey] || [],
        });
      }
    }

    // Add days of the current month (Monday to Friday only)
    for (let day = 1; day <= daysInMonth; day++) {
      const dateObj = new Date(year, month, day);
      // Skip weekends (0 = Sunday, 6 = Saturday)
      if (dateObj.getDay() !== 0 && dateObj.getDay() !== 6) {
        const dateKey = dateObj.toISOString().split('T')[0];
        days.push({
          date: day,
          isCurrentMonth: true,
          dateObj,
          events: eventsByDate[dateKey] || [],
        });
      }
    }

    // Fill remaining cells to complete the grid (only for weekdays)
    const totalWeeks = Math.ceil(days.length / 5);
    const totalCells = totalWeeks * 5;
    const remainingCells = totalCells - days.length;
    
    let nextDay = 1;
    for (let i = 0; i < remainingCells; i++) {
      const nextDate = new Date(year, month + 1, nextDay);
      // Only add weekdays
      if (nextDate.getDay() !== 0 && nextDate.getDay() !== 6) {
        const dateKey = nextDate.toISOString().split('T')[0];
        days.push({
          date: nextDay,
          isCurrentMonth: false,
          dateObj: nextDate,
          events: eventsByDate[dateKey] || [],
        });
      }
      nextDay++;
      // Safety break to avoid infinite loop
      if (nextDay > 31) break;
    }

    return days;
  }, [currentDate, eventsByDate]);

  const navigateMonth = (direction: 'prev' | 'next') => {
    setCurrentDate(prev => {
      const newDate = new Date(prev);
      if (direction === 'prev') {
        newDate.setMonth(prev.getMonth() - 1);
      } else {
        newDate.setMonth(prev.getMonth() + 1);
      }
      return newDate;
    });
  };

  const formatMonthYear = (date: Date) => {
    return date.toLocaleDateString('cs-CZ', { 
      year: 'numeric', 
      month: 'long' 
    });
  };

  const handleEventClick = (event: CalendarEventDto) => {
    if (onEventClick && event.id) {
      onEventClick(event.id);
    }
  };

  const weekDays = ['Po', 'Út', 'St', 'Čt', 'Pá'];

  return (
    <div className="bg-white rounded-lg shadow-sm border border-gray-200">
      {/* Calendar Header */}
      <div className="flex items-center justify-between p-4 border-b border-gray-200">
        <div className="flex items-center space-x-3">
          <Calendar className="h-5 w-5 text-indigo-600" />
          <h2 className="text-lg font-semibold text-gray-900">
            Kalendář výrobních zakázek
          </h2>
        </div>
        <div className="flex items-center space-x-2">
          <button
            onClick={() => navigateMonth('prev')}
            className="p-2 hover:bg-gray-100 rounded-lg transition-colors"
            title="Předchozí měsíc"
          >
            <ChevronLeft className="h-4 w-4 text-gray-600" />
          </button>
          <span className="text-lg font-medium text-gray-900 min-w-[120px] text-center">
            {formatMonthYear(currentDate)}
          </span>
          <button
            onClick={() => navigateMonth('next')}
            className="p-2 hover:bg-gray-100 rounded-lg transition-colors"
            title="Následující měsíc"
          >
            <ChevronRight className="h-4 w-4 text-gray-600" />
          </button>
        </div>
      </div>

      {/* Calendar Content */}
      <div className="p-4">
        {isLoading ? (
          <div className="flex items-center justify-center h-64">
            <div className="flex items-center space-x-2">
              <Loader2 className="h-5 w-5 animate-spin text-indigo-500" />
              <div className="text-gray-500">Načítání kalendáře...</div>
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
            <div className="grid grid-cols-5 gap-1 mb-2">
              {weekDays.map(day => (
                <div 
                  key={day}
                  className="text-center text-sm font-medium text-gray-500 py-2"
                >
                  {day}
                </div>
              ))}
            </div>

            {/* Calendar Grid */}
            <div className="grid grid-cols-5 gap-1">
              {calendarDays.map((day, index) => (
                <div
                  key={index}
                  className={`
                    min-h-[120px] border border-gray-100 p-1 bg-white
                    ${!day.isCurrentMonth ? 'bg-gray-50 text-gray-400' : ''}
                  `}
                >
                  {/* Day Number */}
                  <div className={`
                    text-sm font-medium mb-1
                    ${day.isCurrentMonth ? 'text-gray-900' : 'text-gray-400'}
                  `}>
                    {day.date}
                  </div>

                  {/* Events */}
                  <div className="space-y-1">
                    {day.events.map((event, eventIndex) => (
                      <div
                        key={eventIndex}
                        onClick={() => handleEventClick(event)}
                        className={`
                          text-xs p-1 rounded cursor-pointer transition-all
                          ${event.state ? stateColors[event.state] : ''}
                          hover:shadow-sm hover:scale-105
                        `}
                        title={`${event.title || event.orderNumber}\nZakázka: ${event.orderNumber}\nStav: ${event.state}\n${event.responsiblePerson ? `Odpovědná osoba: ${event.responsiblePerson}` : ''}${event.manualActionRequired ? `\n⚠️ Vyžaduje ruční zásah` : ''}`}
                      >
                        <div className="flex items-center space-x-1">
                          <Factory className="h-3 w-3 flex-shrink-0" />
                          <span className="truncate font-medium">
                            {event.title || event.orderNumber}
                          </span>
                          {event.manualActionRequired && (
                            <div 
                              className="w-2 h-2 bg-red-500 rounded-full flex-shrink-0" 
                              title="Vyžaduje ruční zásah"
                            />
                          )}
                        </div>
                        {event.responsiblePerson && (
                          <div className="flex items-center space-x-1 text-xs opacity-75">
                            <User className="h-2 w-2" />
                            <span className="truncate">
                              {event.responsiblePerson}
                            </span>
                          </div>
                        )}
                      </div>
                    ))}
                  </div>
                </div>
              ))}
            </div>
          </>
        )}
      </div>

      {/* Legend */}
      <div className="border-t border-gray-200 p-4">
        <div className="flex flex-wrap gap-4 text-xs">
          <div className="flex items-center space-x-4 ml-6">
            {Object.entries(stateColors).map(([state, colorClass]) => (
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

export default ManufactureOrderCalendar;