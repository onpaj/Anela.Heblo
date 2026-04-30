import React, { useMemo } from 'react';
import FullCalendar from '@fullcalendar/react';
import dayGridPlugin from '@fullcalendar/daygrid';
import interactionPlugin from '@fullcalendar/interaction';
import csLocale from '@fullcalendar/core/locales/cs';
import type { EventClickArg, EventDropArg, DatesSetArg } from '@fullcalendar/core';
import type { EventResizeDoneArg } from '@fullcalendar/interaction';
import type { CalendarEvent } from './fullcalendarAdapters';
import { toFcEvent, fromFcDates } from './fullcalendarAdapters';
import './marketingCalendar.css';

interface MarketingMonthCalendarProps {
  events: CalendarEvent[];
  initialDate: Date;
  onEventClick: (id: number) => void;
  onEventMove: (id: number, dateFrom: string, dateTo: string) => void;
  onEventResize: (id: number, dateFrom: string, dateTo: string) => void;
  onDateRangeSelect: (dateFrom: string, dateTo: string) => void;
  onDatesSet: (visibleStart: Date, visibleEnd: Date, currentStart: Date) => void;
  calendarRef: React.RefObject<FullCalendar>;
  className?: string;
}

const MarketingMonthCalendar: React.FC<MarketingMonthCalendarProps> = ({
  events,
  initialDate,
  onEventClick,
  onEventMove,
  onEventResize,
  onDateRangeSelect,
  onDatesSet,
  calendarRef,
  className,
}) => {
  const fcEvents = useMemo(() => events.map(toFcEvent), [events]);

  const handleEventClick = (info: EventClickArg) => {
    onEventClick(Number(info.event.id));
  };

  const handleEventDrop = (info: EventDropArg) => {
    const { dateFrom, dateTo } = fromFcDates(
      info.event.start!,
      info.event.end,
    );
    onEventMove(Number(info.event.id), dateFrom, dateTo);
  };

  const handleEventResize = (info: EventResizeDoneArg) => {
    const { dateFrom, dateTo } = fromFcDates(
      info.event.start!,
      info.event.end,
    );
    onEventResize(Number(info.event.id), dateFrom, dateTo);
  };

  const handleSelect = (info: { start: Date; end: Date; jsEvent: MouseEvent | null }) => {
    const { dateFrom, dateTo } = fromFcDates(info.start, info.end);
    onDateRangeSelect(dateFrom, dateTo);
    calendarRef.current?.getApi().unselect();
  };

  const handleDatesSet = (info: DatesSetArg) => {
    onDatesSet(info.start, info.end, info.view.currentStart);
  };

  return (
    <div className={`marketing-calendar h-full${className ? ` ${className}` : ''}`}>
      <FullCalendar
        ref={calendarRef}
        plugins={[dayGridPlugin, interactionPlugin]}
        initialView="fiveWeeks"
        views={{
          fiveWeeks: {
            type: 'dayGrid',
            duration: { weeks: 5 },
          },
        }}
        locale={csLocale}
        initialDate={initialDate}
        headerToolbar={false}
        events={fcEvents}
        editable={true}
        selectable={true}
        selectMirror={true}
        dayMaxEvents={true}
        height="100%"
        eventClick={handleEventClick}
        eventDrop={handleEventDrop}
        eventResize={handleEventResize}
        select={handleSelect}
        datesSet={handleDatesSet}
        eventContent={(arg) => (
          <div className="px-1 text-xs font-medium truncate leading-5">
            {arg.event.title}
          </div>
        )}
      />
    </div>
  );
};

export default MarketingMonthCalendar;
