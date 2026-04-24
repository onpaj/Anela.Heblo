import React from "react";
import { useDroppable } from "@dnd-kit/core";
import type { DayDropData } from "./calendarDndTypes";

interface CalendarDayCellProps {
  dateStr: string; // "YYYY-MM-DD"
  isCurrentMonth: boolean;
  isToday: boolean;
  day: number;
  isHighlighted?: boolean;
  onMouseDown?: (e: React.MouseEvent) => void;
  onMouseEnter?: (e: React.MouseEvent) => void;
  onPointerEnter?: () => void;
}

const CalendarDayCell: React.FC<CalendarDayCellProps> = ({
  dateStr,
  isCurrentMonth,
  isToday,
  day,
  isHighlighted,
  onMouseDown,
  onMouseEnter,
  onPointerEnter,
}) => {
  const { setNodeRef, isOver } = useDroppable({
    id: `day-${dateStr}`,
    data: { date: dateStr } satisfies DayDropData,
  });

  return (
    <div
      ref={setNodeRef}
      data-date={dateStr}
      onMouseDown={onMouseDown}
      onMouseEnter={onMouseEnter}
      onPointerEnter={onPointerEnter}
      className={`
        border-r border-gray-100 last:border-r-0 p-1
        ${!isCurrentMonth ? "bg-gray-50" : ""}
        ${isOver ? "bg-indigo-50" : ""}
        ${isHighlighted ? "bg-indigo-100" : ""}
      `}
    >
      <span
        className={`
          inline-flex items-center justify-center w-6 h-6 text-sm rounded-full
          ${
            isToday
              ? "bg-indigo-600 text-white font-bold"
              : isCurrentMonth
                ? "text-gray-900"
                : "text-gray-400"
          }
        `}
      >
        {day}
      </span>
    </div>
  );
};

export default CalendarDayCell;
