import { BaseResponse, ErrorCodes } from "../types/errors";
import i18n from "../i18n";

/**
 * Formats an error message with parameters
 */
function formatMessage(
  template: string | undefined,
  params?: Record<string, string>,
): string {
  if (!template) return "";
  if (!params) return template;

  let message = template;
  Object.entries(params).forEach(([key, value]) => {
    message = message.replace(new RegExp(`\\{${key}\\}`, "g"), value);
  });
  return message;
}

/**
 * Gets a localized error message for an error code
 */
export function getErrorMessage(
  errorCode: ErrorCodes,
  params?: Record<string, string>,
): string {
  // Special handling for Exception error code
  if (
    errorCode === ErrorCodes.Exception &&
    params?.exceptionType &&
    params?.message
  ) {
    return `Chyba ${params.exceptionType}\n${params.message}`;
  }

  // Get ErrorCode enum name for translation key using Object.entries for reverse mapping
  let enumName: string | undefined;
  for (const [key, value] of Object.entries(ErrorCodes)) {
    if (value === errorCode) {
      enumName = key;
      break;
    }
  }

  if (!enumName) {
    return `Nastala chyba (neznámý kód: ${errorCode})`;
  }

  // Use enum name as translation key
  const messageKey = `errors.${enumName}`;
  const message = i18n.t(messageKey);

  // If translation not found, return generic error with enum name
  if (message === messageKey || !message) {
    return `Nastala chyba (kód: ${enumName})`;
  }

  const formatted = formatMessage(message, params);
  return formatted || `Nastala chyba (kód: ${enumName})`;
}

/**
 * Handles API error responses
 */
export function handleApiError(response: BaseResponse): string {
  if (response.success) {
    return "";
  }

  if (!response.errorCode) {
    return "Nastala neznámá chyba";
  }

  return getErrorMessage(response.errorCode, response.params);
}

/**
 * Checks if a response is an error response
 */
export function isErrorResponse(response: any): response is BaseResponse {
  return (
    response != null &&
    typeof response === "object" &&
    "success" in response &&
    response.success === false
  );
}

/**
 * Extract error message from various error types
 */
export function extractErrorMessage(error: any): string {
  // Handle API error responses
  if (isErrorResponse(error)) {
    return handleApiError(error);
  }

  // Handle axios/fetch response with data
  if (error?.response?.data && isErrorResponse(error.response.data)) {
    return handleApiError(error.response.data);
  }

  // Handle standard Error objects
  if (error instanceof Error) {
    return error.message;
  }

  // Handle string errors
  if (typeof error === "string") {
    return error;
  }

  // Default fallback
  return "Nastala neočekávaná chyba";
}
