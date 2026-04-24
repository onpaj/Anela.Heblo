import { useMemo } from "react";

export interface CalendarEvent {
  id: number;
  title: string;
  actionType: string;
  dateFrom: string; // "YYYY-MM-DD"
  dateTo: string; // "YYYY-MM-DD"
  associatedProducts: string[];
}

export interface EventSegment {
  event: CalendarEvent;
  startCol: number; // 1–7 (Mon=1 … Sun=7)
  endCol: number; // 1–7, inclusive
  weekRow: number; // 0-based week index within the month grid
  lane: number; // 0-based vertical stack position
}

// Returns the Mon-based column (1=Mon, 7=Sun) for a given Date
function toCol(date: Date): number {
  const day = date.getDay(); // 0=Sun
  return day === 0 ? 7 : day;
}

export function useCalendarLayout(
  events: CalendarEvent[],
  year: number,
  month: number, // 0-based (0=Jan)
): EventSegment[] {
  return useMemo(() => {
    if (!events.length) return [];

    // Build week-row boundaries: each row covers Mon–Sun
    const firstOfMonth = new Date(year, month, 1);
    // Monday of the first week shown on the calendar
    const firstMonday = new Date(firstOfMonth);
    const dow = firstOfMonth.getDay(); // 0=Sun
    const offsetToMonday = dow === 0 ? -6 : 1 - dow;
    firstMonday.setDate(firstOfMonth.getDate() + offsetToMonday);

    // Build array of week-row start dates (Mondays)
    const weekStarts: Date[] = [];
    for (let w = 0; w < 6; w++) {
      const monday = new Date(firstMonday);
      monday.setDate(firstMonday.getDate() + w * 7);
      weekStarts.push(monday);
    }

    // Split each event into one segment per week row it touches
    const rawSegments: Omit<EventSegment, "lane">[] = [];

    for (const event of events) {
      const evFrom = new Date(event.dateFrom);
      const evTo = new Date(event.dateTo);

      for (let w = 0; w < weekStarts.length; w++) {
        const weekStart = weekStarts[w]; // Monday
        const weekEnd = new Date(weekStart);
        weekEnd.setDate(weekStart.getDate() + 6); // Sunday

        // Skip weeks that don't overlap this event
        if (evTo < weekStart || evFrom > weekEnd) continue;

        // Clamp to week boundaries
        const segFrom = evFrom < weekStart ? weekStart : evFrom;
        const segTo = evTo > weekEnd ? weekEnd : evTo;

        rawSegments.push({
          event,
          startCol: toCol(segFrom),
          endCol: toCol(segTo),
          weekRow: w,
        });
      }
    }

    // Assign lanes per week row using greedy interval algorithm
    // Sort by startCol within each row, then assign lowest free lane
    const byRow = new Map<number, Omit<EventSegment, "lane">[]>();
    for (const seg of rawSegments) {
      const list = byRow.get(seg.weekRow) ?? [];
      list.push(seg);
      byRow.set(seg.weekRow, list);
    }

    const segments: EventSegment[] = [];

    for (const [, rowSegs] of byRow) {
      const sorted = [...rowSegs].sort((a, b) => a.startCol - b.startCol);
      // laneEnd[i] = endCol of the last segment placed in lane i
      const laneEnd: number[] = [];

      for (const seg of sorted) {
        let lane = laneEnd.findIndex((end) => end < seg.startCol);
        if (lane === -1) {
          lane = laneEnd.length;
        }
        laneEnd[lane] = seg.endCol;
        segments.push({ ...seg, lane });
      }
    }

    return segments;
  }, [events, year, month]);
}
