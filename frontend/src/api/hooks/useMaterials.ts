import { useQuery } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";

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

export function useMaterialsForPurchase(
  searchTerm?: string,
  limit: number = 50,
) {
  return useQuery({
    queryKey: [
      ...QUERY_KEYS.catalog,
      "materials-for-purchase",
      searchTerm,
      limit,
    ],
    queryFn: async () => {
      const apiClient = getAuthenticatedApiClient();
      const searchParams = new URLSearchParams();

      if (searchTerm) {
        searchParams.append("searchTerm", searchTerm);
      }
      searchParams.append("limit", limit.toString());

      const relativeUrl = `/api/catalog/materials-for-purchase${searchParams.toString() ? `?${searchParams.toString()}` : ""}`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: "GET",
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
    queryKey: [...QUERY_KEYS.catalog, "material-by-product-code", productCode],
    queryFn: async () => {
      if (!productCode) return null;

      const apiClient = getAuthenticatedApiClient();
      const searchParams = new URLSearchParams();
      searchParams.append("searchTerm", productCode);
      searchParams.append("limit", "50");

      const relativeUrl = `/api/catalog/materials-for-purchase?${searchParams.toString()}`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: "GET",
      });

      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }

      const data = (await response.json()) as GetMaterialsForPurchaseResponse;

      // Find exact match by productCode
      const exactMatch = data.materials?.find(
        (material) => material.productCode === productCode,
      );
      return exactMatch || null;
    },
    enabled: !!productCode,
    staleTime: 10 * 60 * 1000, // 10 minutes (longer since this is more stable data)
    gcTime: 15 * 60 * 1000, // 15 minutes
  });
}
