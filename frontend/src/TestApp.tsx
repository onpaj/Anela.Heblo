import React from 'react';
import { BrowserRouter as Router } from 'react-router-dom';
import { MsalProvider } from '@azure/msal-react';
import { PublicClientApplication } from '@azure/msal-browser';
import Layout from './components/Layout/Layout';
import WeatherTest from './components/pages/WeatherTest';
import AuthGuard from './components/auth/AuthGuard';
import './i18n';

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

function TestApp() {
  return (
    <div className="App" data-testid="app">
      <MsalProvider instance={msalInstance}>
        <Router>
          <AuthGuard>
            <Layout>
              <WeatherTest />
            </Layout>
          </AuthGuard>
        </Router>
      </MsalProvider>
    </div>
  );
}

export default TestApp;