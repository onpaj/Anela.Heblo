import React from "react";
import type { CalendarEvent } from "./useCalendarLayout";

const ACTION_TYPE_COLORS: Record<string, string> = {
  SocialMedia: "bg-blue-500 text-white",
  Event: "bg-purple-500 text-white",
  Email: "bg-green-500 text-white",
  PR: "bg-yellow-500 text-gray-900",
  Photoshoot: "bg-pink-500 text-white",
  Other: "bg-gray-500 text-white",
};

const BAR_HEIGHT_PX = 22;
const BAR_GAP_PX = 2;
const TOP_OFFSET_PX = 28; // space below date number in day cell

interface MarketingEventBarProps {
  event: CalendarEvent;
  startCol: number; // 1–7
  endCol: number; // 1–7, inclusive
  lane: number;
  onClick: (id: number) => void;
}

const MarketingEventBar: React.FC<MarketingEventBarProps> = ({
  event,
  startCol,
  endCol,
  lane,
  onClick,
}) => {
  const colorClass =
    ACTION_TYPE_COLORS[event.actionType] ?? ACTION_TYPE_COLORS.Other;
  const top = TOP_OFFSET_PX + lane * (BAR_HEIGHT_PX + BAR_GAP_PX);

  return (
    <div
      role="button"
      tabIndex={0}
      onClick={() => onClick(event.id)}
      onKeyDown={(e) => e.key === "Enter" && onClick(event.id)}
      title={event.title}
      style={{
        gridColumnStart: startCol,
        gridColumnEnd: endCol + 1,
        gridRow: 1,
        top,
        height: BAR_HEIGHT_PX,
        position: "absolute",
        left: 2,
        right: 2,
        zIndex: 10,
        pointerEvents: "auto",
      }}
      className={`
        ${colorClass}
        rounded px-1 text-xs font-medium truncate cursor-pointer
        hover:opacity-80 transition-opacity select-none
      `}
    >
      {event.title}
    </div>
  );
};

export const BAR_HEIGHT_PX_EXPORT = BAR_HEIGHT_PX;
export const BAR_GAP_PX_EXPORT = BAR_GAP_PX;
export const TOP_OFFSET_PX_EXPORT = TOP_OFFSET_PX;

export default MarketingEventBar;
