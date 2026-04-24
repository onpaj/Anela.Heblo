import type { CalendarEvent } from "./useCalendarLayout";

export type DragType = "move" | "resize-start" | "resize-end";

export interface MoveDragData {
  type: "move";
  eventId: number;
  event: CalendarEvent;
  originDate: string; // "YYYY-MM-DD" of the day cell where pointer started
}

export interface ResizeStartDragData {
  type: "resize-start";
  eventId: number;
  event: CalendarEvent;
}

export interface ResizeEndDragData {
  type: "resize-end";
  eventId: number;
  event: CalendarEvent;
}

export type CalendarDragData = MoveDragData | ResizeStartDragData | ResizeEndDragData;

export interface DayDropData {
  date: string; // "YYYY-MM-DD"
}

export interface CalendarDndCallbacks {
  onEventMove: (eventId: number, newDateFrom: string, newDateTo: string) => void;
  onEventResize: (eventId: number, newDateFrom: string, newDateTo: string) => void;
  onCreateRange: (dateFrom: string, dateTo: string) => void;
}
