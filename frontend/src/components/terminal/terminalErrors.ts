const NETWORK_FAILURE_PATTERNS = [
  'Failed to fetch',
  'NetworkError',
  'Load failed'
];

const CZECH_FALLBACK = 'Nepodařilo se spojit se serverem. Zkuste to znovu.';

/**
 * Extract a user-friendly error message from an error object.
 * Network failures are normalized to a Czech fallback message.
 *
 * @param error - The error object (typically from a try-catch or Promise rejection)
 * @returns A user-friendly error message, or the Czech fallback for network errors
 */
export function getTerminalErrorMessage(error: unknown): string {
  // If not an Error instance, return fallback
  if (!(error instanceof Error)) {
    return CZECH_FALLBACK;
  }

  // If message is empty, return fallback
  if (!error.message) {
    return CZECH_FALLBACK;
  }

  // If message matches a network failure pattern, return fallback
  if (NETWORK_FAILURE_PATTERNS.some((pattern) => error.message.includes(pattern))) {
    return CZECH_FALLBACK;
  }

  // Otherwise return the error message
  return error.message;
}
