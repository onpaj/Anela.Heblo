import { useQuery } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../client';

interface HealthStatus {
  status: 'Healthy' | 'Unhealthy' | 'Degraded';
  totalDuration: string;
  entries: Record<string, {
    status: 'Healthy' | 'Unhealthy' | 'Degraded';
    duration: string;
    description?: string;
    exception?: string;
    data?: Record<string, any>;
  }>;
}

const fetchHealthStatus = async (endpoint: string): Promise<HealthStatus> => {
  // Use API client with disabled toast notifications for health checks
  const apiClient = await getAuthenticatedApiClient(false);
  const relativeUrl = `/health/${endpoint}`;
  const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;
  
  const response = await (apiClient as any).http.fetch(fullUrl, {
    method: 'GET',
    headers: {
      'Accept': 'application/json',
    }
  });

  if (!response.ok) {
    throw new Error(`Health check failed: ${response.status}`);
  }

  return response.json();
};

export const useLiveHealthCheck = () => {
  return useQuery({
    queryKey: ['health', 'live'],
    queryFn: () => fetchHealthStatus('live'),
    refetchInterval: 15000, // 15 seconds
    refetchOnWindowFocus: true,
    retry: 1,
    staleTime: 7000, // Consider data stale after 7 seconds
  });
};

export const useReadyHealthCheck = () => {
  return useQuery({
    queryKey: ['health', 'ready'],
    queryFn: () => fetchHealthStatus('ready'),
    refetchInterval: 15000, // 15 seconds
    refetchOnWindowFocus: true,
    retry: 1,
    staleTime: 7000, // Consider data stale after 7 seconds
  });
};