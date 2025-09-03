import { useQuery } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../client';
import { GetProductUsageResponse } from '../generated/api-client';

export const useProductUsage = (productCode: string) => {
  return useQuery<GetProductUsageResponse>({
    queryKey: ['product-usage', productCode],
    queryFn: async () => {
      if (!productCode) {
        return new GetProductUsageResponse({ manufactureTemplates: [] });
      }

      const apiClient = getAuthenticatedApiClient();
      return await apiClient.catalog_GetProductUsage(productCode);
    },
    enabled: !!productCode,
    staleTime: 5 * 60 * 1000, // Data is fresh for 5 minutes
    retry: 2
  });
};