import { useQuery } from '@tanstack/react-query';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';

interface ApiClientWithInternals {
  baseUrl: string;
  http: { fetch(url: RequestInfo, init?: RequestInit): Promise<Response> };
}

const fetchOrderTrackingNumbers = async (orderCode: string): Promise<string[]> => {
  try {
    const apiClient = getAuthenticatedApiClient(false) as unknown as ApiClientWithInternals;
    const response = await apiClient.http.fetch(
      `${apiClient.baseUrl}/api/packaging/orders/${encodeURIComponent(orderCode)}/tracking-numbers`,
    );
    if (!response.ok) return [];
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const data = (await response.json()) as any;
    if (!data.success) return [];
    return (data.trackingNumbers as string[] | null) ?? [];
  } catch {
    return [];
  }
};

export const useOrderTrackingNumbers = (orderCode: string, enabled: boolean) =>
  useQuery<string[]>({
    queryKey: [...QUERY_KEYS.orderTrackingNumbers, orderCode],
    queryFn: () => fetchOrderTrackingNumbers(orderCode),
    enabled,
    staleTime: 0,
    retry: false,
  });
