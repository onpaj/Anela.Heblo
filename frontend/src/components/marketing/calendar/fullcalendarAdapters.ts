import type { EventInput } from '@fullcalendar/core';

export interface CalendarEvent {
  id: number;
  title: string;
  actionType: string;
  dateFrom: string; // YYYY-MM-DD
  dateTo: string;   // YYYY-MM-DD, inclusive
  associatedProducts: string[];
  outlookSyncStatus?: string;
}

export const ACTION_TYPE_COLORS: Record<string, { bg: string; text: string }> = {
  SocialMedia: { bg: '#eab308', text: '#111827' }, // Yellow Category
  Blog:        { bg: '#22c55e', text: '#ffffff' }, // Green Category
  Newsletter:  { bg: '#a855f7', text: '#ffffff' }, // Purple Category
  PR:          { bg: '#f97316', text: '#ffffff' }, // Orange Category
  Event:       { bg: '#ef4444', text: '#ffffff' }, // Red Category
  Meeting:     { bg: '#14b8a6', text: '#ffffff' }, // Teal Category
};

export const ACTION_TYPE_TO_INT: Record<string, number> = {
  SocialMedia: 0,
  Blog:        1,
  Newsletter:  2,
  PR:          3,
  Event:       4,
  Meeting:     99,
};

export function formatDateStr(d: Date): string {
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return `${y}-${m}-${day}`;
}

export function toFcEvent(event: CalendarEvent): EventInput {
  const colors = ACTION_TYPE_COLORS[event.actionType] ?? ACTION_TYPE_COLORS.Meeting;

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
      outlookSyncStatus: event.outlookSyncStatus,
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
