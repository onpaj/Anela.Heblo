import React from 'react';
import { BrowserRouter as Router } from 'react-router-dom';
import { MsalProvider } from '@azure/msal-react';
import { PublicClientApplication } from '@azure/msal-browser';
import Layout from './components/Layout/Layout';
import Dashboard from './components/pages/Dashboard';
import AuthGuard from './components/auth/AuthGuard';
import { msalConfig } from './auth/msalConfig';
import './i18n';

// Create MSAL instance
const msalInstance = new PublicClientApplication(msalConfig);

function App() {
  return (
    <div className="App">
      <MsalProvider instance={msalInstance}>
        <Router>
          <AuthGuard>
            <Layout>
              <Dashboard />
            </Layout>
          </AuthGuard>
        </Router>
      </MsalProvider>
    </div>
  );
}

export default App;