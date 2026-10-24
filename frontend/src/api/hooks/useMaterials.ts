import { useQuery } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";
import { IMaterialForPurchaseDto } from "../generated/api-client";

export type MaterialForPurchaseDto = IMaterialForPurchaseDto;

export function useMaterialByProductCode(productCode?: string) {
  return useQuery({
    queryKey: [...QUERY_KEYS.catalog, "material-by-product-code", productCode],
    queryFn: async () => {
      if (!productCode) return null;

      const data = await getAuthenticatedApiClient().catalog_GetMaterialsForPurchase(productCode, 50);

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
