import { useQuery } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../client';

interface ApiClientWithInternals {
  baseUrl: string;
  http: { fetch(url: RequestInfo, init?: RequestInit): Promise<Response> };
}

const fetchOrderTrackingNumber = async (orderCode: string): Promise<string | null> => {
  const apiClient = (await getAuthenticatedApiClient(false)) as unknown as ApiClientWithInternals;
  const response = await apiClient.http.fetch(
    `${apiClient.baseUrl}/api/packaging/orders/${encodeURIComponent(orderCode)}/tracking-number`,
  );
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const data = (await response.json()) as any;
  if (!data.success) return null;
  return (data.trackingNumber as string | null) ?? null;
};

export const useOrderTrackingNumber = (orderCode: string, enabled: boolean) =>
  useQuery<string | null>({
    queryKey: ['order-tracking-number', orderCode],
    queryFn: () => fetchOrderTrackingNumber(orderCode),
    enabled,
    staleTime: 0,
  });
