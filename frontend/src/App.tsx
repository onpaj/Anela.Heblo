import React, { useState, useEffect } from 'react';
import { BrowserRouter as Router } from 'react-router-dom';
import { MsalProvider } from '@azure/msal-react';
import { PublicClientApplication } from '@azure/msal-browser';
import Layout from './components/Layout/Layout';
import WeatherTest from './components/pages/WeatherTest';
import AuthGuard from './components/auth/AuthGuard';
import { StatusBar } from './components/StatusBar';
import { fetchRuntimeConfig, RuntimeConfig } from './config/runtimeConfig';
import './i18n';

function App() {
  const [config, setConfig] = useState<RuntimeConfig | null>(null);
  const [msalInstance, setMsalInstance] = useState<PublicClientApplication | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const initializeApp = async () => {
      try {
        // Fetch runtime configuration
        const runtimeConfig = await fetchRuntimeConfig();
        setConfig(runtimeConfig);

        // Create MSAL configuration with runtime values
        const msalConfig = {
          auth: {
            clientId: runtimeConfig.azureClientId || 'mock-client-id',
            authority: runtimeConfig.azureAuthority || 'https://login.microsoftonline.com/mock-tenant-id',
            redirectUri: window.location.origin,
            postLogoutRedirectUri: window.location.origin,
            clientCapabilities: ['CP1']
          },
          cache: {
            cacheLocation: 'sessionStorage' as const,
            storeAuthStateInCookie: false,
          },
          system: {
            allowNativeBroker: false
          }
        };

        // Create MSAL instance with runtime configuration
        const instance = new PublicClientApplication(msalConfig);
        setMsalInstance(instance);
        
      } catch (err) {
        console.error('Failed to initialize app:', err);
        setError('Failed to load application configuration');
      } finally {
        setLoading(false);
      }
    };

    initializeApp();
  }, []);

  if (loading) {
    return (
      <div className="flex items-center justify-center min-h-screen">
        <div className="text-center">
          <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-indigo-600 mx-auto mb-4"></div>
          <p className="text-gray-600">Loading application configuration...</p>
        </div>
      </div>
    );
  }

  if (error || !config || !msalInstance) {
    return (
      <div className="flex items-center justify-center min-h-screen">
        <div className="text-center">
          <div className="text-red-600 mb-4">
            <svg className="w-8 h-8 mx-auto mb-2" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 8v4m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
            </svg>
          </div>
          <p className="text-gray-600">{error || 'Failed to initialize application'}</p>
        </div>
      </div>
    );
  }

  return (
    <div className="App min-h-screen" data-testid="app">
      <MsalProvider instance={msalInstance}>
        <Router>
          <AuthGuard>
            <Layout statusBar={<StatusBar />}>
              <WeatherTest />
            </Layout>
          </AuthGuard>
        </Router>
      </MsalProvider>
    </div>
  );
}

export default App;