import React from 'react';
import { BrowserRouter as Router } from 'react-router-dom';
import Layout from './components/Layout/Layout';
import './i18n';

function App() {
  return (
    <div className="App">
      <Router>
        <Layout>
          <div className="p-6">
            <h1 className="text-2xl font-bold text-gray-900 mb-4">
              Vítejte v Anela Heblo
            </h1>
            <p className="text-gray-500">
              Aplikace se načítá...
            </p>
          </div>
        </Layout>
      </Router>
    </div>
  );
}

export default App;