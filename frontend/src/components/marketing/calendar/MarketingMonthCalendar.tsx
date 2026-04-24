import React, { useMemo } from "react";
import { DndContext, closestCenter, DragOverlay } from "@dnd-kit/core";
import type { CalendarEvent, EventSegment } from "./useCalendarLayout";
import { useCalendarLayout } from "./useCalendarLayout";
import MarketingEventBar, {
  BAR_HEIGHT_PX_EXPORT as BAR_H,
  BAR_GAP_PX_EXPORT as BAR_G,
  TOP_OFFSET_PX_EXPORT as TOP_OFF,
} from "./MarketingEventBar";
import CalendarDayCell from "./CalendarDayCell";
import CalendarDragOverlay from "./CalendarDragOverlay";
import { useCalendarDnd } from "./useCalendarDnd";
import { useCreateByDrag } from "./useCreateByDrag";
import { toDateString } from "./calendarDateUtils";
import type { CalendarDndCallbacks } from "./calendarDndTypes";

const WEEK_DAYS = ["Po", "Út", "St", "Čt", "Pá", "So", "Ne"];
const BASE_ROW_HEIGHT_PX = 64;

interface MarketingMonthCalendarProps {
  year: number;
  month: number; // 0-based
  events: CalendarEvent[];
  onEventClick: (id: number) => void;
  onEventMove: (eventId: number, newDateFrom: string, newDateTo: string) => void;
  onEventResize: (eventId: number, newDateFrom: string, newDateTo: string) => void;
  onCreateRange: (dateFrom: string, dateTo: string) => void;
  className?: string;
}

interface DayCell {
  date: Date;
  dateStr: string;
  isCurrentMonth: boolean;
  weekRow: number;
  col: number; // 1–7
}

const MarketingMonthCalendar: React.FC<MarketingMonthCalendarProps> = ({
  year,
  month,
  events,
  onEventClick,
  onEventMove,
  onEventResize,
  onCreateRange,
  className,
}) => {
  const today = new Date();

  const callbacks: CalendarDndCallbacks = useMemo(
    () => ({ onEventMove, onEventResize, onCreateRange }),
    [onEventMove, onEventResize, onCreateRange],
  );

  const {
    sensors,
    dragState,
    previewEvents,
    handleDragStart,
    handleDragOver,
    handleDragEnd,
    handleDragCancel,
  } = useCalendarDnd(events, callbacks);

  const segments = useCalendarLayout(previewEvents, year, month);

  // Build day cells
  const { dayCells, weekCount, weekStarts } = useMemo(() => {
    const firstOfMonth = new Date(year, month, 1);
    const firstMonday = new Date(firstOfMonth);
    const dow = firstOfMonth.getDay();
    firstMonday.setDate(firstOfMonth.getDate() + (dow === 0 ? -6 : 1 - dow));

    const cells: DayCell[] = [];
    const starts: Date[] = [];
    let weekRow = 0;
    let col = 1;

    for (let i = 0; i < 42; i++) {
      const date = new Date(firstMonday);
      date.setDate(firstMonday.getDate() + i);
      if (col === 1) starts.push(new Date(date));
      cells.push({
        date,
        dateStr: toDateString(date),
        isCurrentMonth: date.getMonth() === month,
        weekRow,
        col,
      });
      col++;
      if (col > 7) { col = 1; weekRow++; }
    }

    const isWeekAllOutsideMonth = (w: number) =>
      cells.filter((c) => c.weekRow === w).every((c) => !c.isCurrentMonth);

    let lastWeek = 5;
    while (lastWeek > 0 && isWeekAllOutsideMonth(lastWeek)) lastWeek--;

    return {
      dayCells: cells.filter((c) => c.weekRow <= lastWeek),
      weekCount: lastWeek + 1,
      weekStarts: starts.slice(0, lastWeek + 1),
    };
  }, [year, month]);

  // Create-by-drag on empty cells
  const {
    selectionRange,
    handleMouseDown: createMouseDown,
    handleMouseEnter: createMouseEnter,
    handleMouseUp: createMouseUp,
  } = useCreateByDrag(onCreateRange);

  // Row min-heights based on max lane
  const rowMinHeights = useMemo(() => {
    const heights: number[] = Array(weekCount).fill(BASE_ROW_HEIGHT_PX);
    for (const seg of segments) {
      if (seg.weekRow < weekCount) {
        const needed = TOP_OFF + (seg.lane + 1) * (BAR_H + BAR_G) + 4;
        if (needed > heights[seg.weekRow]) heights[seg.weekRow] = needed;
      }
    }
    return heights;
  }, [segments, weekCount]);

  const isToday = (date: Date) =>
    date.getFullYear() === today.getFullYear() &&
    date.getMonth() === today.getMonth() &&
    date.getDate() === today.getDate();

  // Group segments by weekRow
  const segmentsByRow = useMemo(() => {
    const map = new Map<number, EventSegment[]>();
    for (const seg of segments) {
      const list = map.get(seg.weekRow) ?? [];
      list.push(seg);
      map.set(seg.weekRow, list);
    }
    return map;
  }, [segments]);

  // Determine first/last segment keys for each event (for resize handle visibility)
  const eventSegmentBounds = useMemo(() => {
    const bounds = new Map<number, { firstKey: string; lastKey: string }>();
    for (const seg of segments) {
      const key = `${seg.weekRow}-${seg.startCol}`;
      const existing = bounds.get(seg.event.id);
      if (!existing) {
        bounds.set(seg.event.id, { firstKey: key, lastKey: key });
      } else {
        existing.lastKey = key;
      }
    }
    return bounds;
  }, [segments]);

  const isDateInSelection = (dateStr: string) => {
    if (!selectionRange) return false;
    return dateStr >= selectionRange.from && dateStr <= selectionRange.to;
  };

  return (
    <DndContext
      sensors={sensors}
      collisionDetection={closestCenter}
      onDragStart={handleDragStart}
      onDragOver={handleDragOver}
      onDragEnd={handleDragEnd}
      onDragCancel={handleDragCancel}
    >
      {/* eslint-disable-next-line jsx-a11y/no-static-element-interactions */}
      <div
        className={`flex flex-col border border-gray-200 rounded-lg overflow-hidden bg-white${className ? ` ${className}` : ""}`}
        onMouseUp={createMouseUp}
      >
        {/* Header row */}
        <div className="grid grid-cols-7 border-b border-gray-200 flex-shrink-0">
          {WEEK_DAYS.map((day) => (
            <div
              key={day}
              className="py-2 text-center text-xs font-semibold text-gray-500 uppercase tracking-wider"
            >
              {day}
            </div>
          ))}
        </div>

        {/* Week rows */}
        <div className="flex flex-col flex-1">
          {Array.from({ length: weekCount }, (_, w) => {
            const rowCells = dayCells.filter((c) => c.weekRow === w);
            const rowSegs = segmentsByRow.get(w) ?? [];

            return (
              <div
                key={w}
                className="relative flex-1 grid grid-cols-7 border-b border-gray-200 last:border-b-0"
                style={{ minHeight: rowMinHeights[w] }}
              >
                {/* Day cells (droppable) */}
                {rowCells.map((cell) => (
                  <CalendarDayCell
                    key={cell.dateStr}
                    dateStr={cell.dateStr}
                    isCurrentMonth={cell.isCurrentMonth}
                    isToday={isToday(cell.date)}
                    day={cell.date.getDate()}
                    isHighlighted={isDateInSelection(cell.dateStr)}
                    onMouseDown={() => createMouseDown(cell.dateStr)}
                    onMouseEnter={() => createMouseEnter(cell.dateStr)}
                  />
                ))}

                {/* Event bar overlay */}
                <div
                  className="absolute inset-0 grid grid-cols-7"
                  style={{ pointerEvents: "none" }}
                >
                  {rowSegs.map((seg) => {
                    const segKey = `${seg.weekRow}-${seg.startCol}`;
                    const bounds = eventSegmentBounds.get(seg.event.id);
                    return (
                      <MarketingEventBar
                        key={`${seg.event.id}-${seg.weekRow}`}
                        event={seg.event}
                        startCol={seg.startCol}
                        endCol={seg.endCol}
                        lane={seg.lane}
                        weekRowStartDate={weekStarts[w]}
                        isFirstSegment={bounds?.firstKey === segKey}
                        isLastSegment={bounds?.lastKey === segKey}
                        isDragActive={dragState?.eventId === seg.event.id}
                        onClick={onEventClick}
                      />
                    );
                  })}
                </div>
              </div>
            );
          })}
        </div>
      </div>

      <DragOverlay dropAnimation={null}>
        {dragState?.type === "move" ? (
          <CalendarDragOverlay event={dragState.event} />
        ) : null}
      </DragOverlay>
    </DndContext>
  );
};

export default MarketingMonthCalendar;
