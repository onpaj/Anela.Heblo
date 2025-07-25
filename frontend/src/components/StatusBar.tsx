import React, { useState, useEffect } from 'react';
import { getRuntimeConfig } from '../config/runtimeConfig';

interface StatusBarProps {
  className?: string;
  sidebarCollapsed?: boolean;
}

export const StatusBar: React.FC<StatusBarProps> = ({ className = '', sidebarCollapsed = false }) => {
  const [appInfo, setAppInfo] = useState<{
    version: string;
    environment: string;
    apiUrl: string;
    mockAuth: boolean;
  } | null>(null);

  useEffect(() => {
    const loadAppInfo = async () => {
      try {
        const config = getRuntimeConfig();
        
        // Try to fetch configuration from backend API
        try {
          const response = await fetch(`${config.apiUrl}/configuration`);
          if (response.ok) {
            const backendConfig = await response.json();
            setAppInfo({
              version: backendConfig.version,
              environment: backendConfig.environment,
              apiUrl: config.apiUrl,
              mockAuth: backendConfig.useMockAuth
            });
            return;
          }
        } catch (apiError) {
          console.warn('Could not load configuration from backend API:', apiError);
        }
        
        // Fallback to frontend-only configuration
        setAppInfo({
          version: process.env.REACT_APP_VERSION || '0.1.0',
          environment: config.useMockAuth ? 'Development' : 'Production',
          apiUrl: config.apiUrl,
          mockAuth: config.useMockAuth
        });
      } catch (error) {
        console.warn('Could not load app info for status bar:', error);
        setAppInfo({
          version: process.env.REACT_APP_VERSION || '0.1.0',
          environment: process.env.NODE_ENV || 'development',
          apiUrl: window.location.origin,
          mockAuth: true
        });
      }
    };

    loadAppInfo();
  }, []);

  if (!appInfo) {
    return null;
  }

  const getEnvironmentColor = (env: string) => {
    switch (env.toLowerCase()) {
      case 'production':
        return 'text-gray-600 bg-gray-100'; // Default background, primary text color
      case 'test':
        return 'text-white bg-green-600'; // Green background, white text
      default: // Development
        return 'text-black bg-red-600'; // Red background, black text
    }
  };

  const getAuthTypeColor = (mockAuth: boolean) => {
    return mockAuth 
      ? 'text-black bg-yellow-500 px-2 py-0.5 rounded text-xs' // Warning colors for Mock Auth badge
      : 'text-gray-600'; // Normal text for Azure AD
  };

  return (
    <div className={`fixed bottom-0 right-0 bg-gray-100 border-t border-gray-200 text-xs text-gray-600 px-4 py-1 z-10 transition-all duration-300 h-6 ${sidebarCollapsed ? 'left-16' : 'left-64'} ${className}`}>
      <div className="flex items-center justify-end space-x-1">
        <span>{appInfo.version.startsWith('v') ? appInfo.version : `v${appInfo.version}`}</span>
        <span>|</span>
        <span className={`px-2 py-0.5 rounded text-xs ${getEnvironmentColor(appInfo.environment)}`}>
          {appInfo.environment}
        </span>
        {appInfo.mockAuth && (
          <>
            <span>|</span>
            <span className={getAuthTypeColor(appInfo.mockAuth)}>
              Mock Auth
            </span>
          </>
        )}
        <span>|</span>
        <span>API: {new URL(appInfo.apiUrl).host}</span>
      </div>
    </div>
  );
};