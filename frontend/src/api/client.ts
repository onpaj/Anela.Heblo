// API client configuration and TanStack Query integration
import { ApiClient } from './generated/api-client';
import { getRuntimeConfig } from '../config/runtimeConfig';

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

// Create an authenticated API client with token provider
export const getAuthenticatedApiClient = (getAccessToken: () => Promise<string | null>): ApiClient => {
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