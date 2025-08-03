import React from 'react';
import { BrowserRouter as Router } from 'react-router-dom';
import { MsalProvider } from '@azure/msal-react';
import { PublicClientApplication } from '@azure/msal-browser';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import Layout from '../Layout/Layout';
import Dashboard from '../pages/Dashboard';
import AuthGuard from '../auth/AuthGuard';
import '../../i18n';

// Test version of App component with mocked configuration
const msalConfig = {
  auth: {
    clientId: 'mock-client-id',
    authority: 'https://login.microsoftonline.com/mock-tenant-id',
    redirectUri: typeof window !== 'undefined' ? window.location.origin : 'http://localhost:3001',
    postLogoutRedirectUri: typeof window !== 'undefined' ? window.location.origin : 'http://localhost:3001',
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

const msalInstance = new PublicClientApplication(msalConfig);

// Create test query client
const testQueryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: false,
    },
    mutations: {
      retry: false,
    },
  },
});

function TestApp() {
  return (
    <div className="App" data-testid="app">
      <QueryClientProvider client={testQueryClient}>
        <MsalProvider instance={msalInstance}>
          <Router>
            <AuthGuard>
              <Layout>
                <Dashboard />
              </Layout>
            </AuthGuard>
          </Router>
        </MsalProvider>
      </QueryClientProvider>
    </div>
  );
}

export default TestApp;