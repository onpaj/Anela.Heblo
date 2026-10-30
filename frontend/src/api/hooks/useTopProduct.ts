import { useMutation, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";
import {
  SetTopProductRequest,
  SetTopProductResponse,
} from "../generated/api-client";
import { manufacturingStockAnalysisKeys } from "./useManufacturingStockAnalysis";

export interface SetTopProductVariables {
  productCode: string;
  isTopProduct: boolean;
}

// Hook to mark a product as a top product (stored as an attribute in ABRA Flexi)
export const useSetTopProduct = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async ({
      productCode,
      isTopProduct,
    }: SetTopProductVariables): Promise<SetTopProductResponse> => {
      const apiClient = await getAuthenticatedApiClient();
      return await apiClient.catalog_SetTopProduct(
        productCode,
        new SetTopProductRequest({ productCode, isTopProduct }),
      );
    },
    // Returned so the mutation stays pending until the refetch lands and the UI never shows the stale flag
    onSuccess: () =>
      Promise.all([
        queryClient.invalidateQueries({ queryKey: QUERY_KEYS.catalog }),
        queryClient.invalidateQueries({
          queryKey: manufacturingStockAnalysisKeys.all,
        }),
      ]),
  });
};
