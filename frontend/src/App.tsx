import React from 'react';
import { BrowserRouter as Router } from 'react-router-dom';
import { MsalProvider } from '@azure/msal-react';
import { PublicClientApplication } from '@azure/msal-browser';
import Layout from './components/Layout/Layout';
import WeatherTest from './components/pages/WeatherTest';
import AuthGuard from './components/auth/AuthGuard';
import { msalConfig } from './auth/msalConfig';
import './i18n';

// Create MSAL instance
const msalInstance = new PublicClientApplication(msalConfig);

function App() {
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

export default App;