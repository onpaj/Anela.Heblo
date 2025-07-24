import React from 'react';
import { useWeatherQuery, handleApiError } from '../../api/hooks';

const ApiTestComponent: React.FC = () => {
  const { data, isLoading, error } = useWeatherQuery();

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
        <p className="text-red-700">Error: {handleApiError(error)}</p>
      </div>
    );
  }

  return (
    <div className="p-4 bg-green-50 border border-green-200 rounded">
      <h3 className="text-lg font-semibold text-green-800 mb-2">Weather Data</h3>
      {data && data.length > 0 ? (
        <ul className="space-y-2">
          {data.map((forecast, index) => (
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