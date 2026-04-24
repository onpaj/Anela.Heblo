import React, { useMemo } from "react";
import type { CalendarEvent, EventSegment } from "./useCalendarLayout";
import { useCalendarLayout } from "./useCalendarLayout";
import MarketingEventBar, {
  BAR_HEIGHT_PX_EXPORT as BAR_H,
  BAR_GAP_PX_EXPORT as BAR_G,
  TOP_OFFSET_PX_EXPORT as TOP_OFF,
} from "./MarketingEventBar";

const WEEK_DAYS = ["Po", "Út", "St", "Čt", "Pá", "So", "Ne"];

// Minimum row height when no events; grows per lane
const BASE_ROW_HEIGHT_PX = 64;

interface MarketingMonthCalendarProps {
  year: number;
  month: number; // 0-based
  events: CalendarEvent[];
  onEventClick: (id: number) => void;
}

interface DayCell {
  date: Date;
  isCurrentMonth: boolean;
  weekRow: number;
  col: number; // 1–7
}

const MarketingMonthCalendar: React.FC<MarketingMonthCalendarProps> = ({
  year,
  month,
  events,
  onEventClick,
}) => {
  const today = new Date();
  const segments = useCalendarLayout(events, year, month);

  // Build day cells — same Monday-anchor logic as useCalendarLayout
  const { dayCells, weekCount } = useMemo(() => {
    const firstOfMonth = new Date(year, month, 1);
    const firstMonday = new Date(firstOfMonth);
    const dow = firstOfMonth.getDay();
    firstMonday.setDate(firstOfMonth.getDate() + (dow === 0 ? -6 : 1 - dow));

    const cells: DayCell[] = [];
    let weekRow = 0;
    let col = 1;

    // 6 weeks × 7 days
    for (let i = 0; i < 42; i++) {
      const date = new Date(firstMonday);
      date.setDate(firstMonday.getDate() + i);
      cells.push({
        date,
        isCurrentMonth: date.getMonth() === month,
        weekRow,
        col,
      });
      col++;
      if (col > 7) {
        col = 1;
        weekRow++;
      }
    }

    // Trim trailing empty weeks (all outside current month)
    const isWeekAllOutsideMonth = (w: number) =>
      cells.filter((c) => c.weekRow === w).every((c) => !c.isCurrentMonth);

    let lastWeek = 5;
    while (lastWeek > 0 && isWeekAllOutsideMonth(lastWeek)) {
      lastWeek--;
    }

    return {
      dayCells: cells.filter((c) => c.weekRow <= lastWeek),
      weekCount: lastWeek + 1,
    };
  }, [year, month]);

  // Calculate min-height per week row based on max lane used
  const rowMinHeights = useMemo(() => {
    const heights: number[] = Array(weekCount).fill(BASE_ROW_HEIGHT_PX);
    for (const seg of segments) {
      if (seg.weekRow < weekCount) {
        const needed = TOP_OFF + (seg.lane + 1) * (BAR_H + BAR_G) + 4;
        if (needed > heights[seg.weekRow]) {
          heights[seg.weekRow] = needed;
        }
      }
    }
    return heights;
  }, [segments, weekCount]);

  const isToday = (date: Date) =>
    date.getFullYear() === today.getFullYear() &&
    date.getMonth() === today.getMonth() &&
    date.getDate() === today.getDate();

  // Group segments by weekRow for rendering
  const segmentsByRow = useMemo(() => {
    const map = new Map<number, EventSegment[]>();
    for (const seg of segments) {
      const list = map.get(seg.weekRow) ?? [];
      list.push(seg);
      map.set(seg.weekRow, list);
    }
    return map;
  }, [segments]);

  return (
    <div className="border border-gray-200 rounded-lg overflow-hidden bg-white">
      {/* Header row */}
      <div className="grid grid-cols-7 border-b border-gray-200">
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
      {Array.from({ length: weekCount }, (_, w) => {
        const rowCells = dayCells.filter((c) => c.weekRow === w);
        const rowSegs = segmentsByRow.get(w) ?? [];

        return (
          <div
            key={w}
            className="relative grid grid-cols-7 border-b border-gray-200 last:border-b-0"
            style={{ minHeight: rowMinHeights[w] }}
          >
            {/* Background: day cells */}
            {rowCells.map((cell) => (
              <div
                key={cell.date.toISOString()}
                className={`
                  border-r border-gray-100 last:border-r-0 p-1
                  ${!cell.isCurrentMonth ? "bg-gray-50" : ""}
                `}
              >
                <span
                  className={`
                    inline-flex items-center justify-center w-6 h-6 text-sm rounded-full
                    ${
                      isToday(cell.date)
                        ? "bg-indigo-600 text-white font-bold"
                        : cell.isCurrentMonth
                          ? "text-gray-900"
                          : "text-gray-400"
                    }
                  `}
                >
                  {cell.date.getDate()}
                </span>
              </div>
            ))}

            {/* Overlay: event bars — absolutely positioned over the grid */}
            <div
              className="absolute inset-0 grid grid-cols-7"
              style={{ pointerEvents: "none" }}
            >
              {rowSegs.map((seg) => (
                <div
                  key={`${seg.event.id}-${seg.weekRow}`}
                  style={{
                    gridColumn: "1 / -1",
                    position: "relative",
                    gridRow: 1,
                    pointerEvents: "auto",
                  }}
                >
                  <MarketingEventBar
                    event={seg.event}
                    startCol={seg.startCol}
                    endCol={seg.endCol}
                    lane={seg.lane}
                    onClick={onEventClick}
                  />
                </div>
              ))}
            </div>
          </div>
        );
      })}
    </div>
  );
};

export default MarketingMonthCalendar;
