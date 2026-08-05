import { useQuery } from '@tanstack/react-query';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';

const fetchOrderTrackingNumber = async (orderCode: string): Promise<string | null> => {
  try {
    const apiClient = getAuthenticatedApiClient(false);
    const response = await apiClient.packaging_GetOrderTrackingNumber(orderCode);
    if (!response.success) return null;
    return response.trackingNumber ?? null;
  } catch {
    return null;
  }
};

export const useOrderTrackingNumber = (orderCode: string, enabled: boolean) =>
  useQuery<string | null>({
    queryKey: [...QUERY_KEYS.orderTrackingNumber, orderCode],
    queryFn: () => fetchOrderTrackingNumber(orderCode),
    enabled,
    staleTime: 0,
    retry: false,
  });
