import React from 'react';
import { BrowserRouter as Router } from 'react-router-dom';
import Layout from './components/Layout/Layout';
import Dashboard from './components/pages/Dashboard';
import './i18n';

function App() {
  return (
    <div className="App">
      <Router>
        <Layout>
          <Dashboard />
        </Layout>
      </Router>
    </div>
  );
}

export default App;