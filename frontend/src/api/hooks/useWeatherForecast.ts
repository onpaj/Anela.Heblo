import { useQuery } from '@tanstack/react-query';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';

export interface HottestDayDto {
  date: string;
  cityName: string;
  minTemperatureCelsius: number;
  maxTemperatureCelsius: number;
  weatherCode: number;
}

interface GetWeatherForecastApiResponse {
  success: boolean;
  days: HottestDayDto[];
}

const fetchWeatherForecast = async (): Promise<HottestDayDto[]> => {
  const apiClient = getAuthenticatedApiClient();
  const fullUrl = `${(apiClient as any).baseUrl}/api/weather-forecast`;
  const response = await (apiClient as any).http.fetch(fullUrl, {
    method: 'GET',
    headers: { Accept: 'application/json' },
  });
  if (!response.ok) {
    throw new Error(`Weather forecast request failed: ${response.status}`);
  }
  const data: GetWeatherForecastApiResponse = await response.json();
  if (!data.success) {
    throw new Error('Weather forecast unavailable');
  }
  return data.days;
};

export function useWeatherForecast() {
  return useQuery({
    queryKey: QUERY_KEYS.weatherForecast,
    queryFn: fetchWeatherForecast,
    staleTime: 30 * 60 * 1000,
  });
}
