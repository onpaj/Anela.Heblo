import React, { useState, useEffect, useCallback } from 'react';
import { WeatherForecast } from '../../api/generated/api-client';
import { getAuthenticatedApiClient } from '../../api/client';
import { RefreshCw } from 'lucide-react';
import { useAuth } from '../../auth/useAuth';
import { useMockAuth } from '../../auth/mockAuth';
import { shouldUseMockAuth } from '../../config/runtimeConfig';

const WeatherTest: React.FC = () => {
  const [weatherData, setWeatherData] = useState<WeatherForecast[]>([]);
  const [loading, setLoading] = useState<boolean>(false);
  const [error, setError] = useState<string | null>(null);
  
  // Always call both hooks, then choose which one to use
  const realAuth = useAuth();
  const mockAuth = useMockAuth();
  
  // Select the appropriate auth based on configuration
  const auth = shouldUseMockAuth() ? mockAuth : realAuth;

  const fetchWeatherData = useCallback(async () => {
    setLoading(true);
    setError(null);
    
    try {
      const apiClient = getAuthenticatedApiClient(auth.getAccessToken);
      const data = await apiClient.weatherForecast();
      setWeatherData(data);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to fetch weather data');
    } finally {
      setLoading(false);
    }
  }, [auth]);

  useEffect(() => {
    fetchWeatherData();
  }, [fetchWeatherData]);

  const formatDate = (dateString: string) => {
    return new Date(dateString).toLocaleDateString('cs-CZ', {
      weekday: 'long',
      year: 'numeric',
      month: 'long',
      day: 'numeric'
    });
  };

  const getSummaryIcon = (summary?: string) => {
    if (!summary) return '🌤️';
    
    const lowerSummary = summary.toLowerCase();
    if (lowerSummary.includes('hot')) return '🔥';
    if (lowerSummary.includes('warm')) return '☀️';
    if (lowerSummary.includes('cool')) return '🌤️';
    if (lowerSummary.includes('cold')) return '❄️';
    if (lowerSummary.includes('freezing')) return '🥶';
    if (lowerSummary.includes('rain')) return '🌧️';
    if (lowerSummary.includes('snow')) return '❄️';
    return '🌤️';
  };

  return (
    <div className="max-w-4xl mx-auto">
      {/* Header */}
      <div className="mb-8 flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">Weather Forecast</h1>
          <p className="text-gray-500 mt-1">5-day weather forecast from API</p>
        </div>
        
        <button
          onClick={fetchWeatherData}
          disabled={loading}
          className={`inline-flex items-center px-4 py-2 border border-transparent rounded-md shadow-sm text-sm font-medium text-white transition-colors ${
            loading 
              ? 'bg-gray-400 cursor-not-allowed' 
              : 'bg-indigo-600 hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500'
          }`}
        >
          <RefreshCw className={`mr-2 h-4 w-4 ${loading ? 'animate-spin' : ''}`} />
          {loading ? 'Loading...' : 'Reload'}
        </button>
      </div>

      {/* Error State */}
      {error && (
        <div className="mb-6 bg-red-50 border border-red-200 rounded-lg p-4">
          <div className="flex items-center">
            <div className="flex-shrink-0">
              <svg className="h-5 w-5 text-red-400" viewBox="0 0 20 20" fill="currentColor">
                <path fillRule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zM8.707 7.293a1 1 0 00-1.414 1.414L8.586 10l-1.293 1.293a1 1 0 101.414 1.414L10 11.414l1.293 1.293a1 1 0 001.414-1.414L11.414 10l1.293-1.293a1 1 0 00-1.414-1.414L10 8.586 8.707 7.293z" clipRule="evenodd" />
              </svg>
            </div>
            <div className="ml-3">
              <h3 className="text-sm font-medium text-red-800">Error loading weather data</h3>
              <p className="text-sm text-red-700 mt-1">{error}</p>
            </div>
          </div>
        </div>
      )}

      {/* Loading State */}
      {loading && (
        <div className="flex items-center justify-center py-12">
          <div className="flex items-center space-x-2">
            <RefreshCw className="h-6 w-6 animate-spin text-indigo-600" />
            <span className="text-gray-600">Loading weather data...</span>
          </div>
        </div>
      )}

      {/* Weather Cards */}
      {!loading && !error && weatherData.length > 0 && (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
          {weatherData.map((forecast, index) => (
            <div key={index} className="bg-white rounded-lg border border-gray-200 p-6 hover:shadow-md transition-shadow">
              <div className="flex items-center justify-between mb-4">
                <div className="text-4xl">{getSummaryIcon(forecast.summary)}</div>
                <div className="text-right">
                  <div className="text-2xl font-bold text-gray-900">{forecast.temperatureC}°C</div>
                  <div className="text-sm text-gray-500">{forecast.temperatureF}°F</div>
                </div>
              </div>
              
              <div className="space-y-2">
                <div className="text-sm font-medium text-gray-900">
                  {formatDate(forecast.date)}
                </div>
                {forecast.summary && (
                  <div className="text-sm text-gray-600 capitalize">
                    {forecast.summary}
                  </div>
                )}
              </div>
            </div>
          ))}
        </div>
      )}

      {/* Empty State */}
      {!loading && !error && weatherData.length === 0 && (
        <div className="text-center py-12">
          <div className="text-6xl mb-4">🌤️</div>
          <h3 className="text-lg font-medium text-gray-900 mb-2">No weather data available</h3>
          <p className="text-gray-500">Click the reload button to fetch weather data from the API.</p>
        </div>
      )}
    </div>
  );
};

export default WeatherTest;