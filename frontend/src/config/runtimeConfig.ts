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
 * Validate critical environment variables and log missing ones
 */
const validateEnvironmentVariables = (): string[] => {
  const errors: string[] = [];
  const warnings: string[] = [];

  // Critical variables that should always be set
  if (!process.env.REACT_APP_API_URL) {
    warnings.push('REACT_APP_API_URL not set - using window.location.origin as fallback');
  }

  // Authentication variables - only critical if not using mock auth
  const useMockAuth = process.env.REACT_APP_USE_MOCK_AUTH === 'true';
  
  if (!useMockAuth) {
    if (!process.env.REACT_APP_AZURE_CLIENT_ID) {
      errors.push('REACT_APP_AZURE_CLIENT_ID is required for real authentication but not set');
    }
    
    if (!process.env.REACT_APP_AZURE_AUTHORITY) {
      errors.push('REACT_APP_AZURE_AUTHORITY is required for real authentication but not set');
    }
  }

  // Log warnings
  if (warnings.length > 0) {
    console.warn('⚠️  Configuration warnings:');
    warnings.forEach(warning => console.warn(`   - ${warning}`));
  }

  // Log errors
  if (errors.length > 0) {
    console.error('❌ Configuration errors:');
    errors.forEach(error => console.error(`   - ${error}`));
    console.error('   🔄 Falling back to mock authentication mode');
  }

  return errors;
};

/**
 * Load configuration from build-time environment variables
 * This is simpler and more reliable than runtime fetching from backend
 */
export const loadConfig = (): Config => {
  if (cachedConfig) {
    return cachedConfig;
  }

  console.log('🔧 Loading application configuration...');

  // Validate environment variables first
  const configErrors = validateEnvironmentVariables();

  // Determine if we should use mock auth
  const shouldUseMock = process.env.REACT_APP_USE_MOCK_AUTH === 'true' || 
                       !process.env.REACT_APP_AZURE_CLIENT_ID || 
                       !process.env.REACT_APP_AZURE_AUTHORITY ||
                       configErrors.length > 0;

  // Use build-time environment variables set during Docker build
  cachedConfig = {
    apiUrl: process.env.REACT_APP_API_URL || window.location.origin,
    useMockAuth: shouldUseMock,
    azureClientId: process.env.REACT_APP_AZURE_CLIENT_ID || '',
    azureAuthority: process.env.REACT_APP_AZURE_AUTHORITY || '',
  };

  // Log final configuration
  console.log('✅ Configuration loaded successfully:', {
    apiUrl: cachedConfig.apiUrl,
    useMockAuth: cachedConfig.useMockAuth,
    azureClientId: cachedConfig.azureClientId ? '[SET]' : '[NOT SET]',
    azureAuthority: cachedConfig.azureAuthority ? '[SET]' : '[NOT SET]',
  });

  if (cachedConfig.useMockAuth) {
    console.log('🧪 Mock authentication enabled - using fake tokens for API calls');
  } else {
    console.log('🔐 Real authentication enabled - using Microsoft Entra ID');
  }

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