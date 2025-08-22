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
export const setGlobalTokenProvider = (provider: () => Promise<string | null>) => {
  globalTokenProvider = provider;
};

/**
 * Clear the token cache
 * This should be called during logout
 */
export const clearTokenCache = () => {
  tokenCache = null;
  console.log('🗑️ Token cache cleared');
};

/**
 * Centralized authentication header provider with token caching
 * According to documentation specification (Authentication.md section 7.1.3)
 */
const getAuthHeader = async (): Promise<string | null> => {
  if (shouldUseMockAuth()) {
    // Mock authentication - use mockAuthService
    const token = mockAuthService.getAccessToken();
    console.log('🧪 Using cached mock authentication token for API call');
    return `Bearer ${token}`;
  } else {
    // Real authentication - check cache first
    if (isCachedTokenValid()) {
      console.log('⚡ Using cached authentication token for API call');
      return `Bearer ${tokenCache!.token}`;
    }
    
    // Cache miss or expired - get new token
    if (!globalTokenProvider) {
      console.error('❌ Global token provider not set. Make sure App component initialized MSAL.');
      return null;
    }
    
    try {
      console.log('🔐 Acquiring new authentication token...');
      const token = await globalTokenProvider();
      if (token) {
        // Cache the token (MSAL tokens typically expire in 1 hour)
        const expiresAt = Date.now() + (55 * 60 * 1000); // 55 minutes from now
        tokenCache = { token, expiresAt };
        
        console.log('✅ New authentication token acquired and cached');
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
  
  // Create http object with custom fetch that includes authentication
  const authenticatedHttp = {
    fetch: async (url: RequestInfo, init?: RequestInit): Promise<Response> => {
      const authHeader = await getAuthHeader();
      
      const headers: Record<string, string> = {
        ...((init?.headers as Record<string, string>) || {}),
        'Content-Type': 'application/json',
      };
      
      if (authHeader) {
        headers['Authorization'] = authHeader;
      }
      
      return fetch(url, {
        ...init,
        headers,
      });
    }
  };
  
  return new ApiClient(config.apiUrl, authenticatedHttp);
};

// Create an authenticated API client with custom token provider (for advanced scenarios)
export const getAuthenticatedApiClientWithProvider = (getAccessToken: () => Promise<string | null>): ApiClient => {
  const config = getConfig();
  
  const customHttp = {
    fetch: async (url: RequestInfo, init?: RequestInit): Promise<Response> => {
      const token = await getAccessToken();
      
      const headers: Record<string, string> = {
        ...((init?.headers as Record<string, string>) || {}),
        'Content-Type': 'application/json',
      };
      
      if (token) {
        headers['Authorization'] = `Bearer ${token}`;
      }
      
      return fetch(url, {
        ...init,
        headers,
      });
    }
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
  weather: ['weather'] as const,
  catalog: ['catalog'] as const,
  audit: ['audit'] as const,
  productMargins: ['productMargins'] as const,
  productMarginSummary: ['productMarginSummary'] as const,
  financialOverview: ['financialOverview'] as const,
  journal: ['journal'] as const,
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