/**
 * Utility functions for handling date-only values without timezone issues
 */

/**
 * Converts a date string in YYYY-MM-DD format to a Date object that represents the local date
 * without timezone conversion issues.
 * 
 * This prevents the common issue where new Date("2024-01-15") gets interpreted as UTC midnight
 * and then converted to local time, potentially shifting the date by one day.
 */
export function parseLocalDate(dateString: string): Date {
  const [year, month, day] = dateString.split('-').map(Number);
  return new Date(year, month - 1, day); // months are 0-indexed in JavaScript
}

/**
 * Formats a Date object to YYYY-MM-DD string using local date components
 * without timezone conversion issues.
 * 
 * This prevents issues with toISOString() which converts to UTC and may shift the date.
 */
export function formatLocalDate(date: Date): string {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, '0'); // months are 0-indexed
  const day = String(date.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

/**
 * Creates a Date object representing today's date in local time
 */
export function getLocalToday(): Date {
  const now = new Date();
  return new Date(now.getFullYear(), now.getMonth(), now.getDate());
}

/**
 * Safely converts a Date object to a DateOnly-compatible string for backend consumption
 */
export function toDateOnlyString(date: Date | null): string | null {
  if (!date) return null;
  return formatLocalDate(date);
}

/**
 * Safely converts a DateOnly string from backend to a Date object for frontend use
 */
export function fromDateOnlyString(dateString: string | null): Date | null {
  if (!dateString) return null;
  return parseLocalDate(dateString);
}

const TWO_DIGIT_YEAR_BASE = 2000;
const MIN_INPUT_YEAR = 2000;
const MAX_INPUT_YEAR = 2100;
const MAX_DAY_OF_MONTH = 31;

/**
 * Parses a manually typed date ("d.m.yyyy", "d/m/yy", "d-m-yyyy" or ISO "yyyy-mm-dd")
 * into a local Date. A day past the end of its month (e.g. 31.11.) is aligned to
 * the month's last day instead of being rejected. Returns null when the text is not a date,
 * the day exceeds 31, or the year falls outside 2000–2100.
 */
export function parseDateInputClamped(text: string): Date | null {
  const trimmed = text.trim();
  const isoMatch = /^(\d{4})-(\d{1,2})-(\d{1,2})$/.exec(trimmed);
  const czechMatch = /^(\d{1,2})\s*[./-]\s*(\d{1,2})\s*[./-]\s*(\d{2}|\d{4})$/.exec(trimmed);

  let year: number;
  let month: number;
  let day: number;
  if (isoMatch) {
    [year, month, day] = isoMatch.slice(1).map(Number);
  } else if (czechMatch) {
    [day, month, year] = czechMatch.slice(1).map(Number);
    if (czechMatch[3].length === 2) year += TWO_DIGIT_YEAR_BASE;
  } else {
    return null;
  }

  if (month < 1 || month > 12 || day < 1 || day > MAX_DAY_OF_MONTH) return null;
  if (year < MIN_INPUT_YEAR || year > MAX_INPUT_YEAR) return null;

  const lastDayOfMonth = new Date(year, month, 0).getDate();
  return new Date(year, month - 1, Math.min(day, lastDayOfMonth));
}

/**
 * Formats a Date as Czech "dd.mm.yyyy" (zero-padded day and month).
 */
export function formatCzechDate(date: Date): string {
  const day = String(date.getDate()).padStart(2, '0');
  const month = String(date.getMonth() + 1).padStart(2, '0');
  return `${day}.${month}.${date.getFullYear()}`;
}
