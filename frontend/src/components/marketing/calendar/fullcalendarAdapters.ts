import type { EventInput } from '@fullcalendar/core';

export interface CalendarEvent {
  id: number;
  title: string;
  actionType: string;
  dateFrom: string; // YYYY-MM-DD
  dateTo: string;   // YYYY-MM-DD, inclusive
  associatedProducts: string[];
}

export const ACTION_TYPE_COLORS: Record<string, { bg: string; text: string }> = {
  General:   { bg: '#3b82f6', text: '#ffffff' }, // Sociální sítě
  Promotion: { bg: '#a855f7', text: '#ffffff' }, // Událost
  Launch:    { bg: '#22c55e', text: '#ffffff' }, // Email
  Campaign:  { bg: '#eab308', text: '#111827' }, // PR
  Event:     { bg: '#ec4899', text: '#ffffff' }, // Fotografie
  Other:     { bg: '#6b7280', text: '#ffffff' }, // Ostatní
};

export const ACTION_TYPE_TO_INT: Record<string, number> = {
  General:   0,
  Promotion: 1,
  Launch:    2,
  Campaign:  3,
  Event:     4,
  Other:     99,
};

export function formatDateStr(d: Date): string {
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return `${y}-${m}-${day}`;
}

export function toFcEvent(event: CalendarEvent): EventInput {
  const colors = ACTION_TYPE_COLORS[event.actionType] ?? ACTION_TYPE_COLORS.Other;

  // FullCalendar end is exclusive, API dateTo is inclusive → add 1 day
  const [y, mo, d] = event.dateTo.split('-').map(Number);
  const endExclusive = new Date(y, mo - 1, d + 1);

  return {
    id: String(event.id),
    title: event.title,
    start: event.dateFrom,
    end: formatDateStr(endExclusive),
    backgroundColor: colors.bg,
    textColor: colors.text,
    borderColor: colors.bg,
    extendedProps: {
      actionType: event.actionType,
      associatedProducts: event.associatedProducts,
    },
  };
}

export function fromFcDates(
  start: Date,
  end: Date | null,
): { dateFrom: string; dateTo: string } {
  const dateFrom = formatDateStr(start);

  if (!end) {
    return { dateFrom, dateTo: dateFrom };
  }

  // FullCalendar end is exclusive → subtract 1 day for inclusive API dateTo
  const inclusiveEnd = new Date(end);
  inclusiveEnd.setDate(inclusiveEnd.getDate() - 1);

  return { dateFrom, dateTo: formatDateStr(inclusiveEnd) };
}
