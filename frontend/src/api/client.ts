// API client configuration and TanStack Query integration
import { ApiClient } from "./generated/api-client";
import { getConfig, shouldUseMockAuth } from "../config/runtimeConfig";
import { mockAuthService } from "../auth/mockAuth";
import { isE2ETestMode, getE2EAccessToken } from "../auth/e2eAuth";

/**
 * Global toast handler for API errors
 * This will be set by the App component after ToastProvider is initialized
 */
let globalToastHandler: ((title: string, message?: string) => void) | null =
  null;

/**
 * Global token provider for API client
 * This will be set by the App component after MSAL is initialized
 */
let globalTokenProvider: (() => Promise<string | null>) | null = null;

/**
 * Token cache to avoid requesting new tokens for every API call
 */
interface TokenCache {
  token: string;
  expiresAt: number; // Unix timestamp
}

let tokenCache: TokenCache | null = null;

/**
 * Check if cached token is still valid (with 5 minute buffer)
 */
const isCachedTokenValid = (): boolean => {
  if (!tokenCache) return false;
  const now = Date.now();
  const bufferMs = 5 * 60 * 1000; // 5 minutes buffer
  return tokenCache.expiresAt > now + bufferMs;
};

/**
 * Set the global token provider
 * This should be called from App component after MSAL initialization
 */
export const setGlobalTokenProvider = (
  provider: () => Promise<string | null>,
) => {
  globalTokenProvider = provider;
};

/**
 * Set the global toast error handler
 * This should be called from App component after ToastProvider is initialized
 */
export const setGlobalToastHandler = (
  handler: (title: string, message?: string) => void,
) => {
  globalToastHandler = handler;
  console.log("🍞 Global toast handler set for API errors");
};

/**
 * Clear the token cache
 * This should be called during logout
 */
export const clearTokenCache = () => {
  tokenCache = null;
  console.log("🗑️ Token cache cleared");
};

/**
 * Centralized authentication header provider with token caching
 * According to documentation specification (Authentication.md section 7.1.3)
 */
const getAuthHeader = async (): Promise<string | null> => {
  if (isE2ETestMode()) {
    // E2E test mode - use session cookies, no Authorization header needed
    const token = getE2EAccessToken();
    if (token) {
      console.log("🧪 Using E2E authentication token for API call");
      return `Bearer ${token}`;
    } else {
      console.log(
        "🧪 E2E mode: Using session cookies, no Authorization header",
      );
      return null; // No Authorization header - backend validates via cookies
    }
  } else if (shouldUseMockAuth()) {
    // Mock authentication - use mockAuthService
    const token = mockAuthService.getAccessToken();
    console.log("🧪 Using cached mock authentication token for API call");
    return `Bearer ${token}`;
  } else {
    // Real authentication - check cache first
    if (isCachedTokenValid()) {
      console.log("⚡ Using cached authentication token for API call");
      return `Bearer ${tokenCache!.token}`;
    }

    // Cache miss or expired - get new token
    if (!globalTokenProvider) {
      console.error(
        "❌ Global token provider not set. Make sure App component initialized MSAL.",
      );
      return null;
    }

    try {
      console.log("🔐 Acquiring new authentication token...");
      const token = await globalTokenProvider();
      if (token) {
        // Cache the token (MSAL tokens typically expire in 1 hour)
        const expiresAt = Date.now() + 55 * 60 * 1000; // 55 minutes from now
        tokenCache = { token, expiresAt };

        console.log("✅ New authentication token acquired and cached");
        return `Bearer ${token}`;
      } else {
        console.warn(
          "⚠️  No authentication token available - user may need to login",
        );
        return null;
      }
    } catch (error) {
      console.error("❌ Failed to acquire authentication token:", error);
      return null;
    }
  }
};

// Create API client instance with runtime configuration
let apiClient: ApiClient;

export const getApiClient = (): ApiClient => {
  if (!apiClient) {
    const config = getConfig();
    console.log(`🌐 Creating API client with base URL: ${config.apiUrl}`);
    apiClient = new ApiClient(config.apiUrl);
  }
  return apiClient;
};

/**
 * Extract error message from API response and check if it's a structured API error
 */
const extractErrorMessage = async (
  response: Response,
): Promise<{ message: string; isStructuredError: boolean }> => {
  try {
    const contentType = response.headers.get("content-type");
    if (contentType && contentType.includes("application/json")) {
      const errorData = await response.json();

      // Check if this is new BaseResponse structure with errorCode
      if (errorData.success === false && errorData.errorCode) {
        // Import error handler dynamically to avoid circular dependencies
        const { getErrorMessage } = await import("../utils/errorHandler");
        const message = getErrorMessage(errorData.errorCode, errorData.params);
        return {
          message,
          isStructuredError: true,
        };
      }

      // Check if this is old structured API response with success: false and errorMessage
      if (errorData.success === false && errorData.errorMessage) {
        return {
          message: errorData.errorMessage,
          isStructuredError: true,
        };
      }

      // Try common error message fields for unstructured errors
      const message =
        errorData.message ||
        errorData.title ||
        errorData.detail ||
        errorData.error ||
        JSON.stringify(errorData); // Show full body if no known fields

      return { message, isStructuredError: false };
    } else {
      const textResponse = await response.text();
      // If body is empty, show status code with generic message
      const message = textResponse || `API Call Error (${response.status})`;
      return {
        message,
        isStructuredError: false,
      };
    }
  } catch {
    // Final fallback - just status code and generic message
    return {
      message: `API Call Error (${response.status})`,
      isStructuredError: false,
    };
  }
};

// Create an authenticated API client that uses centralized auth header provider
export const getAuthenticatedApiClient = (
  showErrorToasts: boolean = true,
): ApiClient => {
  const config = getConfig();

  // Create http object with custom fetch that includes authentication and error handling
  const authenticatedHttp = {
    fetch: async (url: RequestInfo, init?: RequestInit): Promise<Response> => {
      const authHeader = await getAuthHeader();

      const headers: Record<string, string> = {
        ...((init?.headers as Record<string, string>) || {}),
        "Content-Type": "application/json",
      };

      // Handle E2E authentication with special header
      if (isE2ETestMode()) {
        const e2eToken = getE2EAccessToken();
        if (e2eToken) {
          console.log("🧪 Setting X-E2E-Test-Token header for API call");
          headers["X-E2E-Test-Token"] = e2eToken;
        } else {
          console.log("🧪 E2E mode: Using session cookies, no special headers");
        }
      } else if (authHeader) {
        headers["Authorization"] = authHeader;
      }

      const response = await fetch(url, {
        ...init,
        headers,
        // Include credentials (cookies) for E2E test mode
        credentials: isE2ETestMode() ? "include" : "same-origin",
      });

      // Global error handling with toast notifications
      // Handle both HTTP errors (!response.ok) and business logic errors (success: false)
      const shouldCheckForBusinessErrors = response.ok && showErrorToasts && globalToastHandler;
      const shouldHandleHttpErrors = !response.ok && showErrorToasts && globalToastHandler;
      
      if (shouldHandleHttpErrors || shouldCheckForBusinessErrors) {
        // Clone response to preserve it for SwaggerException
        const responseClone = response.clone();

        try {
          const errorInfo = await extractErrorMessage(responseClone);

          // Show toast for all errors - centralized handling
          console.log(
            `🔍 Error debug - isStructuredError: ${errorInfo.isStructuredError}, message: "${errorInfo.message}"`,
          );

          if (errorInfo.isStructuredError && globalToastHandler) {
            // Structured API error - show ErrorMessage
            console.error(
              `🚨 Structured API Error [${response.status}] ${url}:`,
              errorInfo.message,
            );
            globalToastHandler("Upozornění", errorInfo.message);
          } else if (shouldHandleHttpErrors && globalToastHandler) {
            // Only show unstructured errors for HTTP errors, not for business logic warnings
            const title = `Chyba API (${response.status})`;
            console.error(
              `🚨 Unstructured API Error [${response.status}] ${url}:`,
              errorInfo.message,
            );
            globalToastHandler(title, errorInfo.message);
          }
        } catch (toastError) {
          console.error("🍞 Failed to show error toast:", toastError);
          // Fallback toast only for HTTP errors
          if (shouldHandleHttpErrors && globalToastHandler) {
            globalToastHandler(
              `Chyba API (${response.status})`,
              "Neočekávaná chyba na serveru",
            );
          }
        }
      }

      return response;
    },
  };

  return new ApiClient(config.apiUrl, authenticatedHttp);
};

// Create an authenticated API client with custom token provider (for advanced scenarios)
export const getAuthenticatedApiClientWithProvider = (
  getAccessToken: () => Promise<string | null>,
): ApiClient => {
  const config = getConfig();

  const customHttp = {
    fetch: async (url: RequestInfo, init?: RequestInit): Promise<Response> => {
      const token = await getAccessToken();

      const headers: Record<string, string> = {
        ...((init?.headers as Record<string, string>) || {}),
        "Content-Type": "application/json",
      };

      if (token) {
        headers["Authorization"] = `Bearer ${token}`;
      }

      return fetch(url, {
        ...init,
        headers,
        // Include credentials (cookies) for E2E test mode
        credentials: isE2ETestMode() ? "include" : "same-origin",
      });
    },
  };

  return new ApiClient(config.apiUrl, customHttp);
};

// Legacy export for backward compatibility
export { getApiClient as apiClient };

// API client configuration
export const getApiConfig = () => {
  const config = getConfig();
  return {
    baseUrl: config.apiUrl,
    timeout: 30000, // 30 seconds
  };
};

// Query keys for TanStack Query
export const QUERY_KEYS = {
  weather: ["weather"] as const,
  catalog: ["catalog"] as const,
  audit: ["audit"] as const,
  productMargins: ["productMargins"] as const,
  productMarginSummary: ["productMarginSummary"] as const,
  financialOverview: ["financialOverview"] as const,
  journal: ["journal"] as const,
  transportBox: ["transport-boxes"] as const,
  transportBoxTransitions: ["transportBoxTransitions"] as const,
  manufactureOutput: ["manufacture-output"] as const,
  manufactureDifficulty: ["manufacture-difficulty-settings"] as const,
  manufactureOrders: ["manufactureOrders"] as const,
  productUsage: ["product-usage"] as const,
  health: ["health"] as const,
  giftPackages: ["gift-packages"] as const,
  warehouseStatistics: ["warehouse-statistics"] as const,
  stockTaking: ["stock-taking"] as const,
  userManagement: ["user-management"] as const,
  // Add more query keys as needed
  // users: ['users'] as const,
  // products: ['products'] as const,
} as const;

// Default query options
export const DEFAULT_QUERY_OPTIONS = {
  staleTime: 5 * 60 * 1000, // 5 minutes
  gcTime: 10 * 60 * 1000, // 10 minutes (previously cacheTime)
  retry: 3,
  retryDelay: (attemptIndex: number) =>
    Math.min(1000 * 2 ** attemptIndex, 30000),
};
