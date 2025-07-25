import React, { useState, useEffect } from 'react';
import { BrowserRouter as Router } from 'react-router-dom';
import { MsalProvider } from '@azure/msal-react';
import { PublicClientApplication } from '@azure/msal-browser';
import Layout from './components/Layout/Layout';
import WeatherTest from './components/pages/WeatherTest';
import AuthGuard from './components/auth/AuthGuard';
import { StatusBar } from './components/StatusBar';
import { loadConfig, Config } from './config/runtimeConfig';
import { setGlobalTokenProvider } from './api/client';
import { apiRequest } from './auth/msalConfig';
import './i18n';

function App() {
  const [config, setConfig] = useState<Config | null>(null);
  const [msalInstance, setMsalInstance] = useState<PublicClientApplication | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const initializeApp = async () => {
      try {
        // Load configuration from environment variables
        const appConfig = loadConfig();
        setConfig(appConfig);

        // Create MSAL configuration with app configuration
        const msalConfig = {
          auth: {
            clientId: appConfig.azureClientId || 'mock-client-id',
            authority: appConfig.azureAuthority || 'https://login.microsoftonline.com/mock-tenant-id',
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

        // Create MSAL instance with app configuration
        const instance = new PublicClientApplication(msalConfig);
        setMsalInstance(instance);

        // Set global token provider for API client
        if (!appConfig.useMockAuth) {
          setGlobalTokenProvider(async () => {
            try {
              const accounts = instance.getAllAccounts();
              if (accounts.length === 0) {
                return null;
              }
              
              const account = accounts[0];
              const response = await instance.acquireTokenSilent({
                ...apiRequest,
                account: account,
              });
              
              return response.accessToken;
            } catch (error) {
              console.error('Failed to acquire token in global provider:', error);
              return null;
            }
          });
        }
        
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
          <p className="text-gray-600">Initializing application...</p>
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