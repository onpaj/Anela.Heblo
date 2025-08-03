import { useQuery } from '@tanstack/react-query';
import { getConfig } from '../../config/runtimeConfig';

// Temporary types since API client is incomplete
export interface MaterialForPurchaseDto {
  productCode?: string;
  productName?: string;
  productType?: string;
  lastPurchasePrice?: number;
  location?: string;
  currentStock?: number;
  minimalOrderQuantity?: string;
}

interface GetMaterialsForPurchaseResponse {
  materials?: MaterialForPurchaseDto[];
}

export function useMaterialsForPurchase(searchTerm?: string, limit: number = 50) {
  return useQuery({
    queryKey: ['materials-for-purchase', searchTerm, limit],
    queryFn: async () => {
      const config = getConfig();
      const url = new URL(`${config.apiUrl}/api/catalog/materials-for-purchase`);
      
      if (searchTerm) {
        url.searchParams.append('searchTerm', searchTerm);
      }
      url.searchParams.append('limit', limit.toString());

      const response = await fetch(url.toString(), {
        method: 'GET',
        headers: {
          'Content-Type': 'application/json',
        },
      });

      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }

      return response.json() as Promise<GetMaterialsForPurchaseResponse>;
    },
    enabled: true, // Always enabled, but we can debounce the search term
    staleTime: 5 * 60 * 1000, // 5 minutes
    gcTime: 10 * 60 * 1000, // 10 minutes
  });
}

export function useMaterialByProductCode(productCode?: string) {
  return useQuery({
    queryKey: ['material-by-product-code', productCode],
    queryFn: async () => {
      if (!productCode) return null;
      
      const config = getConfig();
      const url = new URL(`${config.apiUrl}/api/catalog/materials-for-purchase`);
      url.searchParams.append('searchTerm', productCode);
      url.searchParams.append('limit', '50');

      const response = await fetch(url.toString(), {
        method: 'GET',
        headers: {
          'Content-Type': 'application/json',
        },
      });

      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }

      const data = await response.json() as GetMaterialsForPurchaseResponse;
      
      // Find exact match by productCode
      const exactMatch = data.materials?.find(material => material.productCode === productCode);
      return exactMatch || null;
    },
    enabled: !!productCode,
    staleTime: 10 * 60 * 1000, // 10 minutes (longer since this is more stable data)
    gcTime: 15 * 60 * 1000, // 15 minutes
  });
}