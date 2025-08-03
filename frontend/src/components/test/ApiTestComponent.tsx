import React, { useState, useEffect } from 'react';

const ApiTestComponent: React.FC = () => {
  const [apiUrl, setApiUrl] = useState<string>('');
  const [status, setStatus] = useState<'checking' | 'connected' | 'error'>('checking');
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const checkApiConnection = async () => {
      try {
        const baseUrl = process.env.REACT_APP_API_URL || 'http://localhost:5000';
        setApiUrl(baseUrl);
        
        // Simple health check
        const response = await fetch(`${baseUrl}/health/live`);
        if (response.ok) {
          setStatus('connected');
        } else {
          setStatus('error');
          setError(`HTTP ${response.status}: ${response.statusText}`);
        }
      } catch (err) {
        setStatus('error');
        setError(err instanceof Error ? err.message : 'Connection failed');
      }
    };

    checkApiConnection();
  }, []);

  if (status === 'checking') {
    return (
      <div className="p-4 bg-blue-50 border border-blue-200 rounded">
        <p className="text-blue-700">Checking API connection...</p>
      </div>
    );
  }

  if (status === 'error') {
    return (
      <div className="p-4 bg-red-50 border border-red-200 rounded">
        <h3 className="text-lg font-semibold text-red-800 mb-2">API Connection Error</h3>
        <p className="text-red-700">API URL: {apiUrl}</p>
        <p className="text-red-700">Error: {error}</p>
      </div>
    );
  }

  return (
    <div className="p-4 bg-green-50 border border-green-200 rounded">
      <h3 className="text-lg font-semibold text-green-800 mb-2">API Connection Test</h3>
      <p className="text-green-700">✅ Successfully connected to API</p>
      <p className="text-green-700">API URL: {apiUrl}</p>
    </div>
  );
};

export default ApiTestComponent;