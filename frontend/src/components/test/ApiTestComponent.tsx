import React, { useState, useEffect } from 'react';
import { ApiClient, WeatherForecast } from '../../api/generated/api-client';

const ApiTestComponent: React.FC = () => {
  const [data, setData] = useState<WeatherForecast[]>([]);
  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const fetchData = async () => {
      try {
        const apiClient = new ApiClient(process.env.REACT_APP_API_URL);
        const weatherData = await apiClient.weatherForecast();
        setData(weatherData);
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Failed to fetch weather data');
      } finally {
        setIsLoading(false);
      }
    };

    fetchData();
  }, []);

  if (isLoading) {
    return (
      <div className="p-4 bg-blue-50 border border-blue-200 rounded">
        <p className="text-blue-700">Loading weather data...</p>
      </div>
    );
  }

  if (error) {
    return (
      <div className="p-4 bg-red-50 border border-red-200 rounded">
        <p className="text-red-700">Error: {error}</p>
      </div>
    );
  }

  return (
    <div className="p-4 bg-green-50 border border-green-200 rounded">
      <h3 className="text-lg font-semibold text-green-800 mb-2">Weather Data</h3>
      {data && data.length > 0 ? (
        <ul className="space-y-2">
          {data.map((forecast: WeatherForecast, index: number) => (
            <li key={index} className="text-green-700">
              {forecast.date}: {forecast.temperatureC}°C - {forecast.summary}
            </li>
          ))}
        </ul>
      ) : (
        <p className="text-green-700">No weather data available</p>
      )}
    </div>
  );
};

export default ApiTestComponent;