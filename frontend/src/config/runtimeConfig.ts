/**
 * Runtime configuration service for React app
 * Fetches configuration from backend API at startup instead of using build-time environment variables
 * This allows Azure AD secrets to be provided at runtime via server environment variables
 */

export interface RuntimeConfig {
  apiUrl: string;
  useMockAuth: boolean;
  azureClientId: string;
  azureAuthority: string;
}

let cachedConfig: RuntimeConfig | null = null;

/**
 * Fetch runtime configuration from the backend
 * This is called once when the app starts
 */
export const fetchRuntimeConfig = async (): Promise<RuntimeConfig> => {
  if (cachedConfig) {
    return cachedConfig;
  }

  try {
    // Determine the API base URL
    // In development, use the environment variable or default
    // In production, use the current origin
    const apiBaseUrl = process.env.REACT_APP_API_URL || window.location.origin;
    
    const response = await fetch(`${apiBaseUrl}/api/config/client`, {
      method: 'GET',
      headers: {
        'Content-Type': 'application/json',
      },
    });

    if (!response.ok) {
      throw new Error(`Failed to fetch config: ${response.status} ${response.statusText}`);
    }

    const config = await response.json();
    
    cachedConfig = {
      apiUrl: config.apiUrl,
      useMockAuth: config.useMockAuth,
      azureClientId: config.azureClientId,
      azureAuthority: config.azureAuthority,
    };

    console.log('Runtime config loaded:', {
      ...cachedConfig,
      // Don't log the actual client ID for security
      azureClientId: cachedConfig.azureClientId ? '[SET]' : '[NOT SET]',
    });

    return cachedConfig;
  } catch (error) {
    console.error('Failed to fetch runtime config, falling back to build-time environment variables:', error);
    
    // Fallback to build-time environment variables if runtime config fails
    // In production, always use current origin to avoid hardcoded development URLs
    cachedConfig = {
      apiUrl: window.location.origin,
      useMockAuth: process.env.REACT_APP_USE_MOCK_AUTH === 'true' || 
                   !process.env.REACT_APP_AZURE_CLIENT_ID || 
                   !process.env.REACT_APP_AZURE_AUTHORITY,
      azureClientId: process.env.REACT_APP_AZURE_CLIENT_ID || '',
      azureAuthority: process.env.REACT_APP_AZURE_AUTHORITY || '',
    };

    return cachedConfig;
  }
};

/**
 * Get the cached runtime configuration
 * This should only be called after fetchRuntimeConfig() has been called
 */
export const getRuntimeConfig = (): RuntimeConfig => {
  if (!cachedConfig) {
    throw new Error('Runtime config not loaded. Call fetchRuntimeConfig() first.');
  }
  return cachedConfig;
};

/**
 * Check if we should use mock authentication based on runtime config
 */
export const shouldUseMockAuth = (): boolean => {
  const config = getRuntimeConfig();
  return config.useMockAuth || !config.azureClientId || !config.azureAuthority;
};