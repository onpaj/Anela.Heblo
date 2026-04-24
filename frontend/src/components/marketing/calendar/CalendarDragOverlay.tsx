import React from "react";
import type { CalendarEvent } from "./useCalendarLayout";
import { ACTION_TYPE_COLORS } from "./calendarDndTypes";
import { daysBetween } from "./calendarDateUtils";

/** Approximate pixel width per day column — used for ghost bar sizing during drag. */
const APPROX_DAY_WIDTH_PX = 120;

interface CalendarDragOverlayProps {
  event: CalendarEvent;
}

const CalendarDragOverlay: React.FC<CalendarDragOverlayProps> = ({ event }) => {
  const colorClass = ACTION_TYPE_COLORS[event.actionType] ?? ACTION_TYPE_COLORS.Other;
  const durationDays = daysBetween(event.dateFrom, event.dateTo) + 1;
  const width = Math.max(durationDays * APPROX_DAY_WIDTH_PX, APPROX_DAY_WIDTH_PX);

  return (
    <div
      className={`${colorClass} rounded px-2 text-xs font-medium truncate shadow-lg opacity-90`}
      style={{
        height: 22,
        width,
        lineHeight: "22px",
      }}
    >
      {event.title}
    </div>
  );
};

export default CalendarDragOverlay;
