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
  const start = new Date(`${rangeStart}T00:00:00`);
  const end = new Date(`${rangeEnd}T00:00:00`);
  const totalDays = Math.round((end.getTime() - start.getTime()) / 86_400_000) + 1;

  return Array.from({ length: totalDays }, (_, i) => {
    const date = new Date(start.getTime() + i * 86_400_000);
    const dateStr = formatDateStr(date);
    return {
      date: dateStr,
      events: events.filter((e) => e.dateFrom <= dateStr && e.dateTo >= dateStr),
    };
  });
}
