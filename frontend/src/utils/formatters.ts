/**
 * Utility functions for formatting various data types
 */

/**
 * Formats a date string or Date object to a localized date format
 */
export function formatDate(dateValue: string | Date | null | undefined): string {
  if (!dateValue) return '—';

  try {
    const date = new Date(dateValue);
    return date.toLocaleDateString('cs-CZ', {
      day: '2-digit',
      month: '2-digit',
      year: 'numeric',
    });
  } catch {
    return '—';
  }
}

/**
 * Formats a datetime string or Date object to a localized datetime format
 */
export function formatDateTime(dateValue: string | Date | null | undefined): string {
  if (!dateValue) return '—';

  try {
    const date = new Date(dateValue);
    return date.toLocaleString('cs-CZ', {
      day: '2-digit',
      month: '2-digit',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });
  } catch {
    return '—';
  }
}

/**
 * Formats a number as Czech currency (CZK)
 */
export function formatCurrency(amount: number | null): string {
  if (amount === null || amount === undefined) return '-';
  
  return new Intl.NumberFormat('cs-CZ', {
    style: 'currency',
    currency: 'CZK',
  }).format(amount);
}

/**
 * Formats a number with thousand separators
 */
export function formatNumber(num: number | null): string {
  if (num === null || num === undefined) return '-';
  
  return new Intl.NumberFormat('cs-CZ').format(num);
}

/**
 * Formats a percentage value
 */
export function formatPercentage(value: number | null, decimals: number = 2): string {
  if (value === null || value === undefined) return '-';
  
  return new Intl.NumberFormat('cs-CZ', {
    style: 'percent',
    minimumFractionDigits: decimals,
    maximumFractionDigits: decimals,
  }).format(value / 100);
}