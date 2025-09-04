import React, { useState, useEffect } from 'react';
import { getRuntimeConfig } from '../config/runtimeConfig';
import { getAuthenticatedApiClient } from '../api/client';
import { useLiveHealthCheck, useReadyHealthCheck } from '../api/hooks/useHealth';
import { Heart, Shield } from 'lucide-react';

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

  // Health check hooks
  const { 
    data: liveHealthData, 
    isLoading: isLiveHealthLoading, 
    error: liveHealthError 
  } = useLiveHealthCheck();

  const { 
    data: readyHealthData, 
    isLoading: isReadyHealthLoading, 
    error: readyHealthError 
  } = useReadyHealthCheck();

  useEffect(() => {
    const loadAppInfo = async () => {
      try {
        const config = getRuntimeConfig();
        
        // Try to fetch configuration from backend API
        try {
          const apiClient = getAuthenticatedApiClient();
          const relativeUrl = '/api/configuration';
          const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;
          
          const response = await (apiClient as any).http.fetch(fullUrl, {
            method: 'GET',
          });
          
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
      case 'staging':
        return 'text-white bg-orange-500'; // Orange background, white text
      default: // Development
        return 'text-black bg-red-600'; // Red background, black text
    }
  };

  const getAuthTypeColor = (mockAuth: boolean) => {
    return mockAuth 
      ? 'text-black bg-yellow-500 px-2 py-0.5 rounded text-xs' // Warning colors for Mock Auth badge
      : 'text-gray-600'; // Normal text for Azure AD
  };

  const getHealthDotColor = (status: string, isLoading: boolean, hasError: boolean) => {
    if (isLoading) return 'bg-gray-400 animate-pulse';
    if (hasError) return 'bg-red-500';
    switch (status) {
      case 'Healthy':
        return 'bg-emerald-500';
      case 'Degraded':
        return 'bg-yellow-500';
      case 'Unhealthy':
        return 'bg-red-500';
      default:
        return 'bg-gray-400';
    }
  };

  // Mobile status bar should be full width
  const isMobile = window.innerWidth < 768;
  
  return (
    <div className={`fixed bottom-0 bg-gray-100 border-t border-gray-200 text-xs text-gray-600 px-4 py-1 z-10 transition-all duration-300 h-6 ${
      isMobile 
        ? 'left-0 right-0' 
        : (sidebarCollapsed ? 'left-16 right-0' : 'left-64 right-0')
    } ${className}`}>
      <div className={`flex items-center space-x-1 ${isMobile ? 'justify-between' : 'justify-end'}`}>
        {isMobile ? (
          <>
            <span>Anela Heblo {appInfo.version.startsWith('v') ? appInfo.version : `v${appInfo.version}`}</span>
            <span className={`px-2 py-0.5 rounded text-xs ${getEnvironmentColor(appInfo.environment)}`}>
              {appInfo.environment === 'Development' ? 'Dev' : appInfo.environment}
            </span>
            {appInfo.mockAuth && (
              <span className={getAuthTypeColor(appInfo.mockAuth)}>
                Mock
              </span>
            )}
          </>
        ) : (
          <>
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
            <span>|</span>
            {/* Health Check Dots */}
            <div className="flex items-center space-x-2">
              {/* Live Health Check */}
              <div 
                className="group relative flex items-center"
                title={`Live Health: ${isLiveHealthLoading ? 'Načítá...' : liveHealthError ? 'Chyba' : liveHealthData?.status || 'Neznámý'}`}
              >
                <Heart className="h-3 w-3 text-gray-500 mr-1" />
                <div className={`w-2 h-2 rounded-full ${getHealthDotColor(
                  liveHealthData?.status || 'Unknown', 
                  isLiveHealthLoading, 
                  !!liveHealthError
                )}`}></div>
              </div>
              
              {/* Ready Health Check */}
              <div 
                className="group relative flex items-center"
                title={`Ready Health: ${isReadyHealthLoading ? 'Načítá...' : readyHealthError ? 'Chyba' : readyHealthData?.status || 'Neznámý'}`}
              >
                <Shield className="h-3 w-3 text-gray-500 mr-1" />
                <div className={`w-2 h-2 rounded-full ${getHealthDotColor(
                  readyHealthData?.status || 'Unknown', 
                  isReadyHealthLoading, 
                  !!readyHealthError
                )}`}></div>
              </div>
            </div>
          </>
        )}
      </div>
    </div>
  );
};