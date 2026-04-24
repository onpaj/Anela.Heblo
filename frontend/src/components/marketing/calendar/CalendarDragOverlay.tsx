import React from "react";
import type { CalendarEvent } from "./useCalendarLayout";
import { daysBetween } from "./calendarDateUtils";

const ACTION_TYPE_COLORS: Record<string, string> = {
  SocialMedia: "bg-blue-500 text-white",
  Event: "bg-purple-500 text-white",
  Email: "bg-green-500 text-white",
  PR: "bg-yellow-500 text-gray-900",
  Photoshoot: "bg-pink-500 text-white",
  Other: "bg-gray-500 text-white",
};

const DAY_WIDTH_APPROX = 120;

interface CalendarDragOverlayProps {
  event: CalendarEvent;
}

const CalendarDragOverlay: React.FC<CalendarDragOverlayProps> = ({ event }) => {
  const colorClass = ACTION_TYPE_COLORS[event.actionType] ?? ACTION_TYPE_COLORS.Other;
  const durationDays = daysBetween(event.dateFrom, event.dateTo) + 1;
  const width = Math.max(durationDays * DAY_WIDTH_APPROX, DAY_WIDTH_APPROX);

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
