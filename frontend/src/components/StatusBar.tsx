import React, { useState, useEffect } from 'react';
import { getRuntimeConfig } from '../config/runtimeConfig';

interface StatusBarProps {
  className?: string;
}

export const StatusBar: React.FC<StatusBarProps> = ({ className = '' }) => {
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
        const packageJson = require('../../package.json');
        
        setAppInfo({
          version: packageJson.version,
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
        return 'text-green-600 bg-green-50';
      case 'test':
        return 'text-yellow-600 bg-yellow-50';
      default:
        return 'text-blue-600 bg-blue-50';
    }
  };

  const getAuthTypeColor = (mockAuth: boolean) => {
    return mockAuth 
      ? 'text-orange-600 bg-orange-50' 
      : 'text-green-600 bg-green-50';
  };

  return (
    <div className={`bg-gray-50 border-t border-gray-200 px-4 py-2 text-xs text-gray-600 ${className}`}>
      <div className="flex items-center justify-between">
        <div className="flex items-center space-x-4">
          <span className="font-medium">
            Anela Heblo v{appInfo.version}
          </span>
          
          <span className={`px-2 py-1 rounded-full font-medium ${getEnvironmentColor(appInfo.environment)}`}>
            {appInfo.environment}
          </span>
          
          <span className={`px-2 py-1 rounded-full font-medium ${getAuthTypeColor(appInfo.mockAuth)}`}>
            {appInfo.mockAuth ? 'Mock Auth' : 'Azure AD'}
          </span>
        </div>
        
        <div className="text-right text-gray-500">
          <span>API: {new URL(appInfo.apiUrl).host}</span>
        </div>
      </div>
    </div>
  );
};