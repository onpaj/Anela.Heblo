import type { CalendarEvent } from "./useCalendarLayout";

export const ACTION_TYPE_COLORS: Record<string, string> = {
  SocialMedia: "bg-blue-500 text-white",
  Event: "bg-purple-500 text-white",
  Email: "bg-green-500 text-white",
  PR: "bg-yellow-500 text-gray-900",
  Photoshoot: "bg-pink-500 text-white",
  Other: "bg-gray-500 text-white",
};

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
