import React, { useMemo } from 'react';
import FullCalendar from '@fullcalendar/react';
import dayGridPlugin from '@fullcalendar/daygrid';
import interactionPlugin from '@fullcalendar/interaction';
import csLocale from '@fullcalendar/core/locales/cs';
import type { EventClickArg, EventDropArg, DatesSetArg, EventContentArg } from '@fullcalendar/core';
import type { EventResizeDoneArg } from '@fullcalendar/interaction';
import type { CalendarEvent } from './fullcalendarAdapters';
import { toFcEvent, fromFcDates } from './fullcalendarAdapters';
import './marketingCalendar.css';

export type CalendarViewName = 'fiveWeeks' | 'twoWeeks';

const CALENDAR_VIEWS = {
  fiveWeeks: { type: 'dayGrid', duration: { weeks: 5 }, dayMaxEvents: true },
  twoWeeks:  { type: 'dayGrid', duration: { weeks: 2 }, dayMaxEvents: false },
} as const;

const ACTION_TYPE_LABELS: Record<string, string> = {
  SocialMedia: 'SoMe',
  Blog: 'Blog',
  Newsletter: 'NL',
  PR: 'PR',
  Event: 'Akce',
  Meeting: 'Porada',
};

function formatCzechDate(d: Date): string {
  return `${d.getDate()}. ${d.getMonth() + 1}.`;
}

function formatEventDateRange(start: Date, fcEnd: Date | null): string {
  // fcEnd is exclusive in FullCalendar; subtract 1 day to get inclusive display end
  const end = fcEnd ? new Date(fcEnd.getTime() - 86400000) : start;
  const startStr = formatCzechDate(start);
  if (start.toDateString() === end.toDateString()) return startStr;
  return `${startStr} – ${formatCzechDate(end)}`;
}

function formatProductLabel(count: number): string | null {
  if (count === 0) return null;
  if (count === 1) return '1 produkt';
  if (count < 5) return `${count} produkty`;
  return `${count} produktů`;
}

function renderCardEvent(arg: EventContentArg): React.ReactElement {
  const actionType = (arg.event.extendedProps.actionType as string) ?? 'Other';
  const products = (arg.event.extendedProps.associatedProducts as string[]) ?? [];
  const typeLabel = ACTION_TYPE_LABELS[actionType] ?? actionType;
  const dateStr = formatEventDateRange(arg.event.start!, arg.event.end);
  const productLabel = formatProductLabel(products.length);

  return (
    <div className="px-1.5 py-0.5 flex flex-col gap-0.5 w-full overflow-hidden">
      <div className="text-xs font-semibold leading-4 line-clamp-2">
        {arg.event.title}
      </div>
      <div className="flex items-center gap-1 text-[10px] font-medium opacity-90">
        <span className="bg-white/20 rounded px-1 shrink-0">{typeLabel}</span>
        <span className="truncate">
          {dateStr}{productLabel ? ` · ${productLabel}` : ''}
        </span>
      </div>
    </div>
  );
}

function renderCompactEvent(arg: EventContentArg): React.ReactElement {
  return (
    <div className="px-1 text-xs font-medium truncate leading-5">
      {arg.event.title}
    </div>
  );
}

interface MarketingMonthCalendarProps {
  events: CalendarEvent[];
  initialDate: Date;
  viewName: CalendarViewName;
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
  viewName,
  onEventClick,
  onEventMove,
  onEventResize,
  onDateRangeSelect,
  onDatesSet,
  calendarRef,
  className,
}) => {
  const fcEvents = useMemo(() => events.map(toFcEvent), [events]);

  const wrapperClassName = [
    'marketing-calendar',
    viewName === 'twoWeeks' && 'two-weeks',
    'h-full',
    className,
  ]
    .filter(Boolean)
    .join(' ');

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
    <div className={wrapperClassName} data-testid="marketing-calendar-wrapper">
      <FullCalendar
        ref={calendarRef}
        plugins={[dayGridPlugin, interactionPlugin]}
        initialView={viewName}
        views={CALENDAR_VIEWS}
        locale={csLocale}
        initialDate={initialDate}
        headerToolbar={false}
        events={fcEvents}
        editable={true}
        selectable={true}
        selectMirror={true}
        height="100%"
        eventClick={handleEventClick}
        eventDrop={handleEventDrop}
        eventResize={handleEventResize}
        select={handleSelect}
        datesSet={handleDatesSet}
        eventContent={viewName === 'twoWeeks' ? renderCardEvent : renderCompactEvent}
      />
    </div>
  );
};

export default MarketingMonthCalendar;
