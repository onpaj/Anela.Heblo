// API client configuration and TanStack Query integration
import { ApiClient } from './generated/api-client';
import { getRuntimeConfig, shouldUseMockAuth } from '../config/runtimeConfig';
import { mockAuthService } from '../auth/mockAuth';

/**
 * Centralized authentication header provider
 * According to documentation specification (Authentication.md section 7.1.3)
 */
const getAuthHeader = async (): Promise<string | null> => {
  if (shouldUseMockAuth()) {
    // Mock authentication - use mockAuthService
    const token = mockAuthService.getAccessToken();
    return `Bearer ${token}`;
  } else {
    // Real authentication - use MSAL (to be implemented)
    // const token = await msalInstance.acquireTokenSilent(...);
    // return `Bearer ${token.accessToken}`;
    
    // For now, return null as fallback until real auth is implemented
    return null;
  }
};

// Create API client instance with runtime configuration
let apiClient: ApiClient;

export const getApiClient = (): ApiClient => {
  if (!apiClient) {
    // This will be called after runtime config is loaded
    const config = getRuntimeConfig();
    apiClient = new ApiClient(config.apiUrl);
  }
  return apiClient;
};

// Create an authenticated API client that uses centralized auth header provider
export const getAuthenticatedApiClient = (): ApiClient => {
  const config = getRuntimeConfig();
  
  // Token provider that uses centralized getAuthHeader function
  const tokenProvider = async (): Promise<string | null> => {
    const authHeader = await getAuthHeader();
    // Extract token from "Bearer TOKEN" format
    return authHeader ? authHeader.replace('Bearer ', '') : null;
  };
  
  return new ApiClient(config.apiUrl, tokenProvider);
};

// Create an authenticated API client with custom token provider (for advanced scenarios)
export const getAuthenticatedApiClientWithProvider = (getAccessToken: () => Promise<string | null>): ApiClient => {
  const config = getRuntimeConfig();
  return new ApiClient(config.apiUrl, getAccessToken);
};

// Legacy export for backward compatibility
export { getApiClient as apiClient };

// API client configuration
export const getApiConfig = () => {
  const config = getRuntimeConfig();
  return {
    baseUrl: config.apiUrl,
    timeout: 30000, // 30 seconds
  };
};

// Query keys for TanStack Query
export const QUERY_KEYS = {
  weather: ['weather'] as const,
  // Add more query keys as needed
  // users: ['users'] as const,
  // products: ['products'] as const,
} as const;

// Default query options
export const DEFAULT_QUERY_OPTIONS = {
  staleTime: 5 * 60 * 1000, // 5 minutes
  gcTime: 10 * 60 * 1000, // 10 minutes (previously cacheTime)
  retry: 3,
  retryDelay: (attemptIndex: number) => Math.min(1000 * 2 ** attemptIndex, 30000),
};