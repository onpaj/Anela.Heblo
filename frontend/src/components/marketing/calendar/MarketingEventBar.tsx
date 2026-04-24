import React from 'react';
import { useDraggable } from '@dnd-kit/core';
import type { CalendarEvent } from './useCalendarLayout';
import { type CalendarDragData, ACTION_TYPE_COLORS } from './calendarDndTypes';
import { toDateString } from './calendarDateUtils';

export const BAR_HEIGHT_PX = 22;
export const BAR_GAP_PX = 2;
export const TOP_OFFSET_PX = 28;

// Keep backward-compatible exports used by MarketingMonthCalendar
export const BAR_HEIGHT_PX_EXPORT = BAR_HEIGHT_PX;
export const BAR_GAP_PX_EXPORT = BAR_GAP_PX;
export const TOP_OFFSET_PX_EXPORT = TOP_OFFSET_PX;

interface MarketingEventBarProps {
  event: CalendarEvent;
  startCol: number; // 1–7
  endCol: number; // 1–7, inclusive
  lane: number;
  weekRowStartDate: Date; // Monday of this week row
  isFirstSegment: boolean;
  isLastSegment: boolean;
  isDragActive: boolean;
  isResizing: boolean;
  onClick: (id: number) => void;
  onResizePointerDown?: (type: 'start' | 'end', eventId: number) => void;
}

const MarketingEventBar: React.FC<MarketingEventBarProps> = ({
  event,
  startCol,
  endCol,
  lane,
  weekRowStartDate,
  isFirstSegment,
  isLastSegment,
  isDragActive,
  isResizing,
  onClick,
  onResizePointerDown,
}) => {
  const colorClass = ACTION_TYPE_COLORS[event.actionType] ?? ACTION_TYPE_COLORS.Other;
  const top = TOP_OFFSET_PX + lane * (BAR_HEIGHT_PX + BAR_GAP_PX);

  // Compute the date that this bar's startCol represents (startCol=1 → Monday)
  const originDate = new Date(weekRowStartDate);
  originDate.setDate(weekRowStartDate.getDate() + (startCol - 1));
  const originDateStr = toDateString(originDate);

  // Move draggable (body only — resize is handled via pointer events, not dnd-kit)
  const moveData: CalendarDragData = {
    type: 'move',
    eventId: event.id,
    event,
    originDate: originDateStr,
  };
  const {
    attributes: moveAttrs,
    listeners: moveListeners,
    setNodeRef: setMoveRef,
  } = useDraggable({
    id: `move-${event.id}-${startCol}-${lane}`,
    data: moveData,
  });

  return (
    <div
      ref={setMoveRef}
      {...moveAttrs}
      {...moveListeners}
      role="button"
      tabIndex={0}
      onClick={() => onClick(event.id)}
      onKeyDown={(e) => e.key === 'Enter' && onClick(event.id)}
      title={event.title}
      style={{
        gridColumnStart: startCol,
        gridColumnEnd: endCol + 1,
        gridRow: 1,
        top,
        height: BAR_HEIGHT_PX,
        position: 'absolute',
        left: 2,
        right: 2,
        zIndex: 10,
        pointerEvents: isResizing ? 'none' : 'auto',
        opacity: isDragActive ? 0.3 : 1,
      }}
      className={`
        group relative
        ${colorClass}
        rounded px-1 text-xs font-medium truncate cursor-grab
        hover:opacity-80 transition-opacity select-none
      `}
    >
      {/* Left resize handle — pointer events only, no dnd-kit */}
      {isFirstSegment && (
        <div
          onPointerDown={(e) => {
            e.stopPropagation();
            onResizePointerDown?.('start', event.id);
          }}
          className="absolute left-0 top-0 bottom-0 w-3 cursor-col-resize opacity-0 group-hover:opacity-100 bg-white/30 rounded-l"
        />
      )}

      {/* Title text */}
      <span className="relative z-0">{event.title}</span>

      {/* Right resize handle — pointer events only, no dnd-kit */}
      {isLastSegment && (
        <div
          onPointerDown={(e) => {
            e.stopPropagation();
            onResizePointerDown?.('end', event.id);
          }}
          className="absolute right-0 top-0 bottom-0 w-3 cursor-col-resize opacity-0 group-hover:opacity-100 bg-white/30 rounded-r"
        />
      )}
    </div>
  );
};

export default MarketingEventBar;
