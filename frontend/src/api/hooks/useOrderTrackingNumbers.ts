import { useQuery } from '@tanstack/react-query';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';

const fetchOrderTrackingNumbers = async (orderCode: string): Promise<string[]> => {
  try {
    const apiClient = getAuthenticatedApiClient(false);
    const response = await apiClient.packaging_GetOrderTrackingNumbers(orderCode);
    if (!response.success) return [];
    return response.trackingNumbers ?? [];
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
