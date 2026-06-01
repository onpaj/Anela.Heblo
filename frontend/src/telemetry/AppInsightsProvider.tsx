import React, { useEffect } from 'react';
import { useLocation } from 'react-router-dom';
import { AppInsightsContext } from '@microsoft/applicationinsights-react-js';
import { reactPlugin, getAppInsights } from './appInsights';

interface AppInsightsProviderProps {
  children: React.ReactNode;
}

export function AppInsightsProvider({ children }: AppInsightsProviderProps) {
  const location = useLocation();

  useEffect(() => {
    getAppInsights()?.trackPageView({ name: location.pathname });
  }, [location.pathname]);

  return (
    <AppInsightsContext.Provider value={reactPlugin}>
      {children}
    </AppInsightsContext.Provider>
  );
}
