/**
 * Runtime configuration service for React app
 * Loads configuration from backend /api/configuration endpoint at startup
 * Enables single Docker image to work across all environments
 */

export interface Config {
  apiUrl: string;
  useMockAuth: boolean;
  azureClientId: string;
  azureAuthority: string;
  azureTenantId: string;
  version: string;
  environment: string;
}

export class ConfigurationError extends Error {
  constructor(message: string, public readonly originalError?: Error) {
    super(message);
    this.name = 'ConfigurationError';
  }
}

let cachedConfig: Config | null = null;
let configLoadPromise: Promise<Config> | null = null;

/**
 * Get the configuration API URL
 * Development: http://localhost:5000/api/configuration
 * Production: {window.location.origin}/api/configuration
 */
const getConfigurationUrl = (): string => {
  const isDevelopment = process.env.NODE_ENV === 'development';
  if (isDevelopment) {
    return 'http://localhost:5000/api/configuration';
  }
  return `${window.location.origin}/api/configuration`;
};

/**
 * Load configuration from backend /api/configuration endpoint
 * This async function must be called during app initialization before rendering
 */
export const loadConfig = async (): Promise<Config> => {
  // Return cached config if available
  if (cachedConfig) {
    console.log('✅ Using cached configuration');
    return cachedConfig;
  }

  // Return existing load promise if already loading
  if (configLoadPromise) {
    console.log('⏳ Configuration loading already in progress...');
    return configLoadPromise;
  }

  console.log('🔧 Loading runtime configuration from backend...');

  const configUrl = getConfigurationUrl();
  console.log(`   Fetching from: ${configUrl}`);

  configLoadPromise = fetch(configUrl, {
    method: 'GET',
    headers: {
      'Content-Type': 'application/json',
    },
    credentials: 'omit', // Don't send cookies for config endpoint
  })
    .then(async (response) => {
      if (!response.ok) {
        throw new ConfigurationError(
          `Failed to load configuration: ${response.status} ${response.statusText}`
        );
      }
      return response.json();
    })
    .then((data) => {
      // Map backend response to frontend config format
      cachedConfig = {
        apiUrl: data.apiUrl || window.location.origin,
        useMockAuth: data.useMockAuth || false,
        azureClientId: data.azureClientId || '',
        azureAuthority: data.azureAuthority || '',
        azureTenantId: data.azureTenantId || '',
        version: data.version || 'unknown',
        environment: data.environment || 'unknown',
      };

      console.log('✅ Configuration loaded successfully:', {
        apiUrl: cachedConfig.apiUrl,
        useMockAuth: cachedConfig.useMockAuth,
        azureClientId: cachedConfig.azureClientId ? '[SET]' : '[NOT SET]',
        azureAuthority: cachedConfig.azureAuthority ? '[SET]' : '[NOT SET]',
        azureTenantId: cachedConfig.azureTenantId ? '[SET]' : '[NOT SET]',
        version: cachedConfig.version,
        environment: cachedConfig.environment,
      });

      if (cachedConfig.useMockAuth) {
        console.log('🧪 Mock authentication enabled - using fake tokens for API calls');
      } else {
        console.log('🔐 Real authentication enabled - using Microsoft Entra ID');
      }

      configLoadPromise = null; // Clear promise after successful load
      return cachedConfig;
    })
    .catch((error) => {
      configLoadPromise = null; // Clear promise on error so retry is possible
      const configError = new ConfigurationError(
        'Failed to load application configuration from backend',
        error
      );
      console.error('❌ Configuration loading failed:', error);
      throw configError;
    });

  return configLoadPromise;
};

/**
 * Get the configuration
 * IMPORTANT: Configuration must be loaded first via loadConfig() during app initialization
 * Throws error if configuration has not been loaded yet
 */
export const getConfig = (): Config => {
  if (!cachedConfig) {
    throw new ConfigurationError(
      'Configuration has not been loaded yet. Call loadConfig() during app initialization.'
    );
  }
  return cachedConfig;
};

/**
 * Check if configuration has been loaded
 */
export const isConfigLoaded = (): boolean => {
  return cachedConfig !== null;
};

/**
 * Check if we should use mock authentication
 */
export const shouldUseMockAuth = (): boolean => {
  if (!cachedConfig) {
    throw new ConfigurationError(
      'Configuration has not been loaded yet. Call loadConfig() during app initialization.'
    );
  }
  return cachedConfig.useMockAuth || !cachedConfig.azureClientId || !cachedConfig.azureAuthority;
};

// Legacy exports for backward compatibility
export type RuntimeConfig = Config;
export const fetchRuntimeConfig = async (): Promise<Config> => loadConfig();
export const getRuntimeConfig = (): Config => getConfig();
