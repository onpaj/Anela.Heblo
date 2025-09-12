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