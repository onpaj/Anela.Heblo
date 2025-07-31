// API client configuration and TanStack Query integration
import { ApiClient } from './generated/api-client';
import { getConfig, shouldUseMockAuth } from '../config/runtimeConfig';
import { mockAuthService } from '../auth/mockAuth';

/**
 * Global token provider for API client
 * This will be set by the App component after MSAL is initialized
 */
let globalTokenProvider: (() => Promise<string | null>) | null = null;

/**
 * Set the global token provider
 * This should be called from App component after MSAL initialization
 */
export const setGlobalTokenProvider = (provider: () => Promise<string | null>) => {
  globalTokenProvider = provider;
};

/**
 * Centralized authentication header provider
 * According to documentation specification (Authentication.md section 7.1.3)
 */
const getAuthHeader = async (): Promise<string | null> => {
  if (shouldUseMockAuth()) {
    // Mock authentication - use mockAuthService
    const token = mockAuthService.getAccessToken();
    console.log('🧪 Using mock authentication token for API call');
    return `Bearer ${token}`;
  } else {
    // Real authentication - use global token provider
    if (!globalTokenProvider) {
      console.error('❌ Global token provider not set. Make sure App component initialized MSAL.');
      return null;
    }
    
    try {
      console.log('🔐 Acquiring real authentication token...');
      const token = await globalTokenProvider();
      if (token) {
        console.log('✅ Real authentication token acquired successfully');
        return `Bearer ${token}`;
      } else {
        console.warn('⚠️  No authentication token available - user may need to login');
        return null;
      }
    } catch (error) {
      console.error('❌ Failed to acquire authentication token:', error);
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

// Create an authenticated API client that uses centralized auth header provider
export const getAuthenticatedApiClient = (): ApiClient => {
  const config = getConfig();
  
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
  const config = getConfig();
  return new ApiClient(config.apiUrl, getAccessToken);
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
  weather: ['weather'] as const,
  catalog: ['catalog'] as const,
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