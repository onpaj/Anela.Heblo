import { formatDateStr } from './fullcalendarAdapters';
import type { CalendarEvent } from './fullcalendarAdapters';

export interface AgendaDay {
  date: string;
  events: CalendarEvent[];
}

export function groupEventsByDay(
  events: CalendarEvent[],
  rangeStart: string,
  rangeEnd: string,
): AgendaDay[] {
  const days: AgendaDay[] = [];
  const current = new Date(`${rangeStart}T00:00:00`);
  const end = new Date(`${rangeEnd}T00:00:00`);

  while (current <= end) {
    const dateStr = formatDateStr(current);
    days.push({
      date: dateStr,
      events: events.filter((e) => e.dateFrom <= dateStr && e.dateTo >= dateStr),
    });
    current.setDate(current.getDate() + 1);
  }

  return days;
}
