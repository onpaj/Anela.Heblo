// API client configuration and TanStack Query integration
import { ApiClient } from './generated/api-client';

// Create API client instance
const apiBaseUrl = process.env.REACT_APP_API_BASE_URL || 'http://localhost:5000';
export const apiClient = new ApiClient(apiBaseUrl);

// API client configuration
export const API_CONFIG = {
  baseUrl: apiBaseUrl,
  timeout: 30000, // 30 seconds
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