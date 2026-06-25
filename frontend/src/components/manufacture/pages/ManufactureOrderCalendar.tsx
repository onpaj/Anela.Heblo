import React, { useState, useMemo } from "react";
import { Calendar } from "lucide-react";
import {
  useManufactureOrderCalendarQuery,
  CalendarEventDto,
} from "../../../api/hooks/useManufactureOrders";
import { stateColors } from "../../../constants/manufactureOrderStates";
import LoadingState from "../../common/LoadingState";
import ErrorState from "../../common/ErrorState";
import CalendarNavigation from "../calendar/CalendarNavigation";
import CalendarEventCard from "../calendar/CalendarEventCard";

interface ManufactureOrderCalendarProps {
  onEventClick?: (orderId: number) => void;
}


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
    <div className="bg-white dark:bg-graphite-surface rounded-lg shadow-sm dark:shadow-soft-dark border border-gray-200 dark:border-graphite-border">
      {/* Calendar Header */}
      <div className="flex items-center justify-between p-4 border-b border-gray-200 dark:border-graphite-border">
        <div className="flex items-center space-x-3">
          <Calendar className="h-5 w-5 text-indigo-600 dark:text-graphite-accent" />
          <h2 className="text-lg font-semibold text-gray-900 dark:text-graphite-text">
            Kalendář výrobních zakázek
          </h2>
        </div>
        <CalendarNavigation
          onPrevious={() => navigateMonth('prev')}
          onNext={() => navigateMonth('next')}
          currentPeriodLabel={formatMonthYear(currentDate)}
          previousTitle="Předchozí měsíc"
          nextTitle="Následující měsíc"
          size="md"
        />
      </div>

      {/* Calendar Content */}
      <div className="p-4">
        {isLoading ? (
          <LoadingState message="Načítání kalendáře..." />
        ) : error ? (
          <ErrorState message="Chyba při načítání kalendáře" />
        ) : (
          <>
            {/* Week Days Header */}
            <div className="grid grid-cols-5 gap-1 mb-2">
              {weekDays.map(day => (
                <div 
                  key={day}
                  className="text-center text-sm font-medium text-gray-500 dark:text-graphite-muted py-2"
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
                    min-h-[120px] border border-gray-100 dark:border-graphite-border p-1 bg-white dark:bg-graphite-surface
                    ${!day.isCurrentMonth ? 'bg-gray-50 text-gray-400 dark:bg-graphite-surface-2 dark:text-graphite-faint' : ''}
                  `}
                >
                  {/* Day Number */}
                  <div className={`
                    text-sm font-medium mb-1
                    ${day.isCurrentMonth ? 'text-gray-900 dark:text-graphite-text' : 'text-gray-400 dark:text-graphite-faint'}
                  `}>
                    {day.date}
                  </div>

                  {/* Events */}
                  <div className="space-y-1">
                    {day.events.map((event, eventIndex) => (
                      <CalendarEventCard
                        key={eventIndex}
                        event={event}
                        variant="compact"
                        onClick={() => handleEventClick(event)}
                      />
                    ))}
                  </div>
                </div>
              ))}
            </div>
          </>
        )}
      </div>

      {/* Legend */}
      <div className="border-t border-gray-200 dark:border-graphite-border p-4">
        <div className="flex flex-wrap gap-4 text-xs">
          <div className="flex items-center space-x-4 ml-6">
            {Object.entries(stateColors).map(([state, colorClass]) => (
              <div key={state} className="flex items-center space-x-1">
                <div
                  className={`w-3 h-3 rounded ${colorClass
                    .split(' ')
                    .filter((c) => c.startsWith("bg-") || c.startsWith("dark:bg-"))
                    .join(" ")} border dark:border-graphite-border`}
                ></div>
                <span className="text-gray-600 dark:text-graphite-muted capitalize">
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