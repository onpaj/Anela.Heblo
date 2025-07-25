/**
 * Configuration service for React app
 * Uses build-time environment variables set during Docker build process
 * This is simpler and more reliable than runtime config fetching
 */

export interface Config {
  apiUrl: string;
  useMockAuth: boolean;
  azureClientId: string;
  azureAuthority: string;
}

let cachedConfig: Config | null = null;

/**
 * Load configuration from build-time environment variables
 * This is simpler and more reliable than runtime fetching from backend
 */
export const loadConfig = (): Config => {
  if (cachedConfig) {
    return cachedConfig;
  }

  // Use build-time environment variables set during Docker build
  cachedConfig = {
    apiUrl: process.env.REACT_APP_API_URL || window.location.origin,
    useMockAuth: process.env.REACT_APP_USE_MOCK_AUTH === 'true' || 
                 !process.env.REACT_APP_AZURE_CLIENT_ID || 
                 !process.env.REACT_APP_AZURE_AUTHORITY,
    azureClientId: process.env.REACT_APP_AZURE_CLIENT_ID || '',
    azureAuthority: process.env.REACT_APP_AZURE_AUTHORITY || '',
  };

  console.log('Configuration loaded from build-time environment variables:', {
    ...cachedConfig,
    // Don't log the actual client ID for security
    azureClientId: cachedConfig.azureClientId ? '[SET]' : '[NOT SET]',
  });

  return cachedConfig;
};

/**
 * Get the configuration
 * Loads from environment variables if not already cached
 */
export const getConfig = (): Config => {
  return loadConfig();
};

/**
 * Check if we should use mock authentication
 */
export const shouldUseMockAuth = (): boolean => {
  const config = getConfig();
  return config.useMockAuth || !config.azureClientId || !config.azureAuthority;
};

// Legacy exports for backward compatibility
export type RuntimeConfig = Config;
export const fetchRuntimeConfig = async (): Promise<Config> => loadConfig();
export const getRuntimeConfig = (): Config => getConfig();