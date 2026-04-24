/** Format a Date as "YYYY-MM-DD" without timezone offset issues. */
export function toDateString(date: Date): string {
  const y = date.getFullYear();
  const m = String(date.getMonth() + 1).padStart(2, "0");
  const d = String(date.getDate()).padStart(2, "0");
  return `${y}-${m}-${d}`;
}

/** Parse "YYYY-MM-DD" to a local Date (avoids UTC offset of new Date(string)). */
export function parseDateString(dateStr: string): Date {
  const [y, m, d] = dateStr.split("-").map(Number);
  return new Date(y, m - 1, d);
}

/** Add days to a "YYYY-MM-DD" string, returns new "YYYY-MM-DD". */
export function addDaysToDateString(dateStr: string, days: number): string {
  const date = parseDateString(dateStr);
  date.setDate(date.getDate() + days);
  return toDateString(date);
}

/** Number of days from dateA to dateB (positive if B is after A). */
export function daysBetween(dateA: string, dateB: string): number {
  const a = parseDateString(dateA);
  const b = parseDateString(dateB);
  const msPerDay = 86_400_000;
  return Math.round((b.getTime() - a.getTime()) / msPerDay);
}

/** If fromDate > toDate, return toDate. Otherwise return fromDate. */
export function clampDateString(fromDate: string, toDate: string): string {
  return fromDate > toDate ? toDate : fromDate;
}
